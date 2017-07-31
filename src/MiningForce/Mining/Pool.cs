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
using MiningForce.Banning;
using MiningForce.Blockchain;
using MiningForce.Blockchain.Bitcoin;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.JsonRpc;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using MiningForce.Stratum;
using MiningForce.Util;
using MiningForce.VarDiff;
using Newtonsoft.Json;
using NLog;

namespace MiningForce.Mining
{
    public class Pool : StratumServer
    {
        public Pool(IComponentContext ctx,
			JsonSerializerSettings serializerSettings,
	        IConnectionFactory cf,
			IStatsRepository statsRepo) : 
            base(ctx)
        {
	        Contract.RequiresNonNull(ctx, nameof(ctx));
	        Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
	        Contract.RequiresNonNull(cf, nameof(cf));
	        Contract.RequiresNonNull(statsRepo, nameof(statsRepo));

			Shares = shareSubject.AsObservable();
	        this.serializerSettings = serializerSettings;

			this.cf = cf;
	        this.statsRepo = statsRepo;
        }

		private PoolConfig poolConfig;
	    private ClusterConfig clusterConfig;
	    private readonly JsonSerializerSettings serializerSettings;
	    private readonly IConnectionFactory cf;
	    private readonly IStatsRepository statsRepo;
        private readonly PoolStats poolStats = new PoolStats();
        private object currentJobParams;
        private IBlockchainJobManager manager;
	    private IBanManager banManager;

		protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers = 
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected readonly ConditionalWeakTable<StratumClient, WorkerContext> workerContexts = 
            new ConditionalWeakTable<StratumClient, WorkerContext>();

        private static readonly string[] HashRateUnits = { " KH", " MH", " GH", " TH", " PH" };
		private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);

		private readonly Subject<IShare> shareSubject = new Subject<IShare>();

		// Telemetry
		private readonly Subject<int> resposeTimesSubject = new Subject<int>();
	    private readonly Subject<IShare> validSharesSubject = new Subject<IShare>();
	    private readonly Subject<Unit> invalidSharesSubject = new Subject<Unit>();

	    #region API-Surface

		public IObservable<IShare> Shares { get; }

	    public void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
	    {
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
		    Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

		    this.logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinJobManager), poolConfig);
			this.poolConfig = poolConfig;
		    this.clusterConfig = clusterConfig;
	    }

		public async Task StartAsync()
        {
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			logger.Info(() => $"[{LogCat}] Launching ...");

	        SetupBanning(clusterConfig);
	        SetupTelemetry();
			await InitializeJobManager();
	        StartListeners(poolConfig.Ports);
	        SetupStats();

			logger.Info(() => $"[{LogCat}] Online");

			OutputPoolInfo();
        }

	    #endregion // API-Surface

        private async Task InitializeJobManager()
        {
            manager = ctx.ResolveKeyed<IBlockchainJobManager>(poolConfig.Coin.Type);

			manager.Configure(poolConfig, clusterConfig);
            await manager.StartAsync(this);

            manager.Jobs
				.Subscribe(OnNewJob);

			// we need work before opening the gates
	        await manager.Jobs.Take(1).ToTask();
        }

	    protected override string LogCat => "Pool";

	    protected override void OnConnect(StratumClient client)
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
		        logger.Trace(() => $"[{LogCat}] [{client.ConnectionId}] Disconnecting banned worker @ {client.RemoteEndpoint.Address}");

				DisconnectClient(client);
	        }
		}

		protected override void OnDisconnect(string subscriptionId)
        {
            // update stats
            lock (clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }
        }

	    protected override void OnRequest(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;
			logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received request {request.Method} [{request.Id}]: {JsonConvert.SerializeObject(request.Params, serializerSettings)}");

		    try
		    {
			    switch (request.Method)
			    {
				    case StratumMethod.Subscribe:
					    OnSubscribe(client, tsRequest);
					    break;

					case StratumMethod.Authorize:
					    OnAuthorize(client, tsRequest);
					    break;

					case StratumMethod.SubmitShare:
					    OnSubmitShare(client, tsRequest);
					    break;

					default:
					    logger.Warn(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

					    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
					    break;
			    }
		    }

		    catch (Exception ex)
		    {
			    logger.Error(ex, () => $"{nameof(OnRequest)}: {request.Method}");
		    }
	    }

	    private async void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
	        var request = tsRequest.Value;
			var requestParams = request.Params?.ToObject<string[]>();
			var response = await manager.SubscribeWorkerAsync(client);

			// respond with manager provided payload
            var data = new object[]
            {
                new object[]
                {
                    new object[] { StratumMethod.SetDifficulty, client.ConnectionId },
	                new object[] { StratumMethod.MiningNotify, client.ConnectionId }
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
            client.Notify(StratumMethod.SetDifficulty, new object[] { context.Difficulty });
            client.Notify(StratumMethod.MiningNotify, currentJobParams);
        }

        private async void OnAuthorize(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
	        var request = tsRequest.Value;
            var context = GetWorkerContext(client);

            var requestParams = request.Params?.ToObject<string[]>();
            var workername = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;

			// assumes that workerName is an address
            context.IsAuthorized = await manager.ValidateAddressAsync(workername);
            client.Respond(context.IsAuthorized, request.Id);
        }

	    private async void OnSubmitShare(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    // check age of submission (aged submissions usually caused by high server load)
		    var requestAge = DateTime.UtcNow - tsRequest.Timestamp;

		    if (requestAge > maxShareAge)
		    {
			    logger.Debug(()=> $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
				return;
		    }

			// check worker state
		    var request = tsRequest.Value;
			var context = GetWorkerContext(client);
            context.LastActivity = DateTime.UtcNow;

            if (!context.IsAuthorized)
                client.RespondError(StratumError.UnauthorizedWorker, "Unauthorized worker", request.Id);
            else if (!context.IsSubscribed)
                client.RespondError(StratumError.NotSubscribed, "Not subscribed", request.Id);
            else
            {
                UpdateVarDiff(client, manager.BlockchainStats.NetworkDifficulty);

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
			            poolStats.LastPoolBlockTime = DateTime.UtcNow;

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
					if (poolConfig.Banning?.Enabled == true)
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
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

        private void UpdateVarDiff(StratumClient client, double networkDifficulty)
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
                var newDiff = varDiffManager.Update(context.VarDiff, context.Difficulty, networkDifficulty);
	            if (newDiff != null)
					context.EnqueueNewDifficulty(newDiff.Value);
            }
        }

        private void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;
            BroadcastJob(currentJobParams);
        }

        private void BroadcastJob(object jobParams)
        {
            BroadcastNotification(StratumMethod.MiningNotify, jobParams, client =>
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
			                logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {context.Difficulty}");

							client.Notify(StratumMethod.SetDifficulty, new object[] {context.Difficulty});
		                }

		                // send job
		                client.Notify(StratumMethod.MiningNotify, currentJobParams);
	                }

					else
					{
						logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");

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
				    var result = shares.Sum(share => (share.NormalizedDifficulty * Math.Pow(2, 32)) / poolHashRateSampleInterval);
					return (float) result;
			    })
			    .Subscribe(hashRate => poolStats.PoolHashRate = hashRate);

			// Periodically persist pool- and blockchain-stats to persistent storage
			Observable.Interval(TimeSpan.FromSeconds(10))
				.StartWith(0)	// initial update
			    .Subscribe(_ => PersistStats());
	    }

	    private void PersistStats()
	    {
		    try
		    {
				logger.Debug(()=> $"[{LogCat}] Persisting stats");

			    cf.RunTx((con, tx) =>
			    {
				    statsRepo.UpdatePoolStats(con, tx, poolConfig.Id, poolStats, manager.BlockchainStats);
			    });
			}

			catch (Exception ex)
		    {
			    logger.Error(ex, ()=> $"[{LogCat}] Unable to persist stats");
		    }
	    }

	    private void SetupTelemetry()
	    {
			// Average response times per minute
		    resposeTimesSubject
			    .Select(ms => (float) ms)
			    .Buffer(TimeSpan.FromMinutes(1))
			    .Select(responses => responses.Count > 0 ? responses.Average() : -1)
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
				    logger.Warn(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

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

Mining Pool:            {poolConfig.Id} 
Coin Type:		{poolConfig.Coin.Type} 
Network Connected:      {manager.BlockchainStats.NetworkType}
Detected Reward Type:   {manager.BlockchainStats.RewardType}
Current Block Height:   {manager.BlockchainStats.BlockHeight}
Current Connect Peers:  {manager.BlockchainStats.ConnectedPeers}
Network Difficulty:     {manager.BlockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatHashRate(manager.BlockchainStats.NetworkHashRate)}
Stratum Port(s):        {string.Join(", ", poolConfig.Ports.Keys)}
Pool Fee:               {poolConfig.RewardRecipients.Sum(x => x.Percentage)}%
";

            logger.Info(()=> msg);
        }
    }
}
