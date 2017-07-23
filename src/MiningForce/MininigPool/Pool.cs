using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using NLog;
using MiningForce.Blockchain;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using MiningForce.Networking.Banning;
using MiningForce.Stratum;
using MiningForce.VarDiff;
using Newtonsoft.Json;

namespace MiningForce.MininigPool
{
    public class Pool : StratumServer
    {
        public Pool(IComponentContext ctx,
	        PoolConfig poolConfig, ClusterConfig clusterConfig,
			JsonSerializerSettings serializerSettings) : 
            base(ctx, LogManager.GetCurrentClassLogger(), serializerSettings)
        {
	        this.poolConfig = poolConfig;
	        this.clusterConfig = clusterConfig;

	        Shares = shareSubject.AsObservable();
        }

		private readonly PoolConfig poolConfig;
	    private readonly ClusterConfig clusterConfig;
        private readonly PoolStats poolStats = new PoolStats();
        private object currentJobParams;
        private IBlockchainJobManager manager;
	    private IBanManager banManager;

		protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers = 
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected readonly ConditionalWeakTable<StratumClient, WorkerContext> workerContexts = 
            new ConditionalWeakTable<StratumClient, WorkerContext>();

        private static readonly string[] HashRateUnits = { " KH", " MH", " GH", " TH", " PH" };

	    private readonly Subject<IShare> shareSubject = new Subject<IShare>();

		// Telemetry
		private readonly Subject<int> resposeTimesSubject = new Subject<int>();
	    private readonly Subject<IShare> validSharesSubject = new Subject<IShare>();
	    private readonly Subject<Unit> invalidSharesSubject = new Subject<Unit>();

	    #region API-Surface

		public NetworkStats NetworkStats => manager.NetworkStats;
        public PoolStats PoolStats => poolStats;
		public IObservable<IShare> Shares { get; }

        public async Task StartAsync()
        {
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			logger.Info(() => $"[{poolConfig.Coin.Type}] Starting ...");

	        SetupBanning(clusterConfig);
	        SetupTelemetry();
	        SetupStats();
			await InitializeJobManager();
	        StartListeners(poolConfig.Ports);

			logger.Info(() => $"[{poolConfig.Coin.Type}] Online");

			OutputPoolInfo();
        }

	    #endregion // API-Surface

        private async Task InitializeJobManager()
        {
            manager = ctx.ResolveKeyed<IBlockchainJobManager>(poolConfig.Coin.Type,
	            new TypedParameter(typeof(PoolConfig), poolConfig),
	            new TypedParameter(typeof(ClusterConfig), clusterConfig));

            await manager.StartAsync(this);

            manager.Jobs.Subscribe(OnNewJob);

			// we need work before opening the gates
	        await manager.Jobs.Take(1).ToTask();
        }

        protected override void OnClientConnected(StratumClient client)
        {
	        if (banManager?.IsBanned(client.RemoteEndpoint.Address) == false)
	        {
		        // expect miner to establish communication within a certain time
		        EnsureNoZombieClient(client);

		        // update stats
		        lock (clients)
		        {
			        poolStats.ConnectedMiners = clients.Count;
		        }

		        // Telemetry
		        client.ResponseTime.Subscribe(x => resposeTimesSubject.OnNext(x));
	        }

	        else
	        {
		        logger.Trace(() => $"[{poolConfig.Coin.Type}] [{client.ConnectionId}] Disconnecting banned worker @ {client.RemoteEndpoint.Address}");

				DisconnectClient(client);
	        }
		}

		protected override void OnClientDisconnected(string subscriptionId)
        {
            // update stats
            lock (clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }
        }

        protected override async void OnClientSubscribe(StratumClient client, JsonRpcRequest request)
        {
			var requestParams = request.Params?.ToObject<string[]>();
			var response = await manager.SubscribeWorkerAsync(client);

			// respond with manager provided payload
            var data = new object[]
            {
                new object[]
                {
                    new object[]
                    {
                        StratumConstants.MsgMiningNotify, client.ConnectionId
                    },
                },
            }
            .Concat(response)
            .ToArray();

	        client.Respond(data, request.Id);

			// setup worker context
			var context = GetWorkerContext(client);
	        context.IsSubscribed = true;
	        context.UserAgent = requestParams?.Length > 0 ? requestParams[0] : null;

			// send intial update
            client.Notify(StratumConstants.MsgSetDifficulty, new object[] { context.Difficulty });
            client.Notify(StratumConstants.MsgMiningNotify, currentJobParams);
        }

        protected override async void OnClientAuthorize(StratumClient client, JsonRpcRequest request)
        {
            var context = GetWorkerContext(client);

            var requestParams = request.Params?.ToObject<string[]>();
            var workername = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;

            context.IsAuthorized = await manager.AuthenticateWorkerAsync(client, workername, password);
            client.Respond(context.IsAuthorized, request.Id);
        }

        protected override async void OnClientSubmitShare(StratumClient client, JsonRpcRequest request)
        {
            var context = GetWorkerContext(client);
            context.LastActivity = DateTime.UtcNow;

            if (!context.IsAuthorized)
                client.RespondError(StratumError.UnauthorizedWorker, "Unauthorized worker", request.Id);
            else if (!context.IsSubscribed)
                client.RespondError(StratumError.NotSubscribed, "Not subscribed", request.Id);
            else
            {
                UpdateVarDiff(client);

	            try
	            {
		            // submit 
		            var requestParams = request.Params?.ToObject<string[]>();
		            var share = await manager.SubmitShareAsync(client, requestParams, context.Difficulty);

		            client.Respond(true, request.Id);

					// record it
					shareSubject.OnNext(share);

					// update pool stats
		            if (share.IsBlockCandidate)
			            poolStats.LastBlockTime = DateTime.UtcNow;

					// update client stats
					context.Stats.ValidShares++;

					// telemetry
					validSharesSubject.OnNext(share);
				}

				catch (StratumException ex)
	            {
		            client.RespondError(ex.Code, ex.Message, request.Id, false);

		            // update client stats
		            context.Stats.InvalidShares++;

		            // telemetry
		            invalidSharesSubject.OnNext(Unit.Default);

					// banning
					if(poolConfig.Banning?.Enabled == true)
						ConsiderBan(client, context, poolConfig.Banning);
	            }
			}
        }

	    private WorkerContext GetWorkerContext(StratumClient client)
        {
            WorkerContext context;

            lock (workerContexts)
            {
                if (!workerContexts.TryGetValue(client, out context))
                {
                    context = new WorkerContext(client, poolConfig);
                    workerContexts.Add(client, context);
                }
            }

            return context;
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            var isAlive = client.Requests
                .Take(1)
                .Select(_ => true);

            var timeout = Observable.Timer(DateTime.UtcNow.AddSeconds(10))
                .Select(_ => false);

            Observable.Merge(isAlive, timeout)
                .Take(1)
                .Subscribe(alive =>
                {
                    if (!alive)
                    {
                        logger.Info(() => $"[{poolConfig.Coin.Type}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        private void UpdateVarDiff(StratumClient client)
        {
            var context = GetWorkerContext(client);

            if (context.VarDiff != null)
            {
                // get or create manager
                VarDiffManager varDiffManager;

                lock (varDiffManagers)
                {
                    if (!varDiffManagers.TryGetValue(client.PoolEndpoint, out varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(client.PoolEndpoint.VarDiff);
                        varDiffManagers[client.PoolEndpoint] = varDiffManager;
                    }
                }

                // update it
                var newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty);
	            if (newDiff != null)
					context.EnqueueNewDifficulty(newDiff.Value);
            }
        }

        private void OnNewJob(object jobParams)
        {
            logger.Debug(() => $"[{poolConfig.Coin.Type}] Received new job params from manager");

            currentJobParams = jobParams;

            BroadcastJob(currentJobParams);
        }

        private void BroadcastJob(object jobParams)
        {
            BroadcastNotification(StratumConstants.MsgMiningNotify, jobParams, client =>
            {
                var context = GetWorkerContext(client);

                if (context.IsSubscribed)
                {
					// check if turned zombie
	                var lastActivityAgo = DateTime.UtcNow - context.LastActivity;

					if (poolConfig.ClientConnectionTimeout == 0 || 
						lastActivityAgo.TotalSeconds < poolConfig.ClientConnectionTimeout)
	                {
		                // varDiff: if the client has a pending difficulty change, apply it now
		                if (context.ApplyPendingDifficulty())
		                {
			                logger.Debug(() => $"[{poolConfig.Coin.Type}] [{client.ConnectionId}] VarDiff update to {context.Difficulty}");

							client.Notify(StratumConstants.MsgSetDifficulty, new object[] {context.Difficulty});
		                }

		                // send job
		                client.Notify(StratumConstants.MsgMiningNotify, currentJobParams);
	                }

					else
					{
						logger.Info(() => $"[{poolConfig.Coin.Type}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");

						DisconnectClient(client);
					}
				}

				return false;
            });
        }

	    private void SetupBanning(ClusterConfig clusterConfig)
	    {
		    if (poolConfig.Banning?.Enabled == true)
		    {
			    var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
				banManager = ctx.ResolveKeyed<IBanManager>(managerType);
		    }
	    }

	    private void SetupStats()
	    {
		    poolStats.PoolFeePercent = (float)poolConfig.RewardRecipients
			    .Where(x => x.Type == RewardRecipientType.Op)
			    .Sum(x => x.Percentage);

			poolStats.DonationsPercent = (float) poolConfig.RewardRecipients
			    .Where(x => x.Type == RewardRecipientType.Dev)
			    .Sum(x => x.Percentage);

		    // Pool Hashrate
		    var poolHashRateSampleInterval = 30;

		    validSharesSubject
			    .Buffer(TimeSpan.FromSeconds(poolHashRateSampleInterval))
			    .Select(shares =>
			    {
				    var result = shares.Sum(share => (share.DifficultyNormalized * Math.Pow(2, 32)) / poolHashRateSampleInterval);
					return (float) result;
			    })
			    .Subscribe(hashRate => poolStats.HashRate = hashRate);
		}

		private void SetupTelemetry()
	    {
			// Average response times per minute
		    resposeTimesSubject
			    .Select(ms => (float) ms)
			    .Buffer(TimeSpan.FromMinutes(1))
			    .Select(responses => responses.Count > 0 ? responses.Average() : 0)
			    .Subscribe(avg => poolStats.AverageResponseTimePerMinuteMs = avg);

		    // Shares per second
			Observable.Merge(validSharesSubject.Select(_=> Unit.Default), invalidSharesSubject)
				.Buffer(TimeSpan.FromSeconds(1))
				.Select(shares => shares.Count)
				.Subscribe(count => poolStats.SharesPerSecond = count);

			// Valid/Invalid shares per minute
			validSharesSubject
				.Buffer(TimeSpan.FromMinutes(1))
			    .Select(shares => shares.Count)
			    .Subscribe(count => poolStats.ValidSharesPerMinute = count);

		    invalidSharesSubject
			    .Buffer(TimeSpan.FromMinutes(1))
			    .Select(shares => shares.Count)
			    .Subscribe(count => poolStats.InvalidSharesPerMinute = count);
		}

	    private void ConsiderBan(StratumClient client, WorkerContext context, PoolBanningConfig config)
	    {
		    var totalShares = context.Stats.ValidShares + context.Stats.InvalidShares;

		    if (totalShares > config.CheckThreshold)
		    {
				var ratioBad = (double) context.Stats.InvalidShares / totalShares;

			    if (ratioBad < config.InvalidPercent / 100.0)
			    {
					// reset stats
				    context.Stats.ValidShares = 0;
				    context.Stats.InvalidShares = 0;
				}

			    else
			    {
				    logger.Warn(() => $"[{poolConfig.Coin.Type}] [{client.ConnectionId}] Banning worker: {Math.Floor(ratioBad * 100)}% of the last {totalShares} were invalid");

				    banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

				    DisconnectClient(client);
				}
			}
	    }

		private static string FormatHashRate(double hashrate)
        {
            var i = -1;

            do
            {
                hashrate = hashrate / 1024;
                i++;
            } while (hashrate > 1024);
            return (int)Math.Abs(hashrate) + HashRateUnits[i];
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Coin.Type} 
Network Connected:      {NetworkStats.Network}
Detected Reward Type:   {NetworkStats.RewardType}
Current Block Height:   {NetworkStats.BlockHeight}
Current Connect Peers:  {NetworkStats.ConnectedPeers}
Network Difficulty:     {NetworkStats.Difficulty}
Network Hash Rate:      {FormatHashRate(NetworkStats.HashRate)}
Stratum Port(s):        {string.Join(", ", poolConfig.Ports.Keys)}
Pool Fee:               {poolConfig.RewardRecipients.Sum(x => x.Percentage)}%
";

            logger.Info(()=> msg);
        }
    }
}
