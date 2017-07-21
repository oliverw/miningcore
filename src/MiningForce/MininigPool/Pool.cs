using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using NLog;
using MiningForce.Blockchain;
using MiningForce.Configuration;
using MiningForce.Configuration.Extensions;
using MiningForce.JsonRpc;
using MiningForce.Stratum;
using MiningForce.VarDiff;
using Newtonsoft.Json;
using NLog.LayoutRenderers.Wrappers;

// TODO:
// - periodic job re-broadcast
// - periodic update of pool hashrate
// - banning based on invalid shares

namespace MiningForce.MininigPool
{
    public class Pool : StratumServer
    {
        public Pool(IComponentContext ctx, 
            JsonSerializerSettings serializerSettings) : 
            base(ctx, LogManager.GetCurrentClassLogger(), serializerSettings)
        {
        }

        private readonly PoolStats poolStats = new PoolStats();

        private object currentJobParams;
        private readonly object currentJobParamsLock = new object();
        private IBlockchainJobManager manager;

        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers = 
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected readonly ConditionalWeakTable<StratumClient, PoolClientContext> miningContexts = 
            new ConditionalWeakTable<StratumClient, PoolClientContext>();

        private static readonly string[] HashRateUnits = { " KH", " MH", " GH", " TH", " PH" };

		// Telemetry
		private readonly Subject<int> resposeTimesSubject = new Subject<int>();
	    private readonly Subject<Unit> validSharesSubject = new Subject<Unit>();
	    private readonly Subject<Unit> invalidSharesSubject = new Subject<Unit>();

		#region API-Surface

		public NetworkStats NetworkStats => manager.NetworkStats;
        public PoolStats PoolStats => poolStats;

        public async Task StartAsync(PoolConfig poolConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            this.poolConfig = poolConfig;

            logger.Info(() => $"[{poolConfig.Coin.Type}] starting ...");

	        SetupTelemetry();
            StartListeners();
            await InitializeJobManager();

            OutputPoolInfo();
        }

	    #endregion // API-Surface

        private async Task InitializeJobManager()
        {
            manager = ctx.ResolveKeyed<IBlockchainJobManager>(poolConfig.Coin.Type,
	            new TypedParameter(typeof(PoolConfig), poolConfig));

            await manager.StartAsync(this);

            manager.Jobs.Subscribe(OnNewJob);
        }

        protected override void OnClientConnected(StratumClient client)
        {
            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(client);

            // update stats
            lock (clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }

	        // Telemetry
	        client.ResponseTime.Subscribe(x=> resposeTimesSubject.OnNext(x));
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
            // var requestParams = request.Params?.ToObject<string[]>();
            // var userAgent = requestParams?.Length > 0 ? requestParams[0] : null;
            var response = await manager.HandleWorkerSubscribeAsync(client);

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

            // send response
            client.Respond(data, request.Id);

            // get or create context
            var context = GetMiningContext(client);
	        context.IsSubscribed = true;

            // send difficulty
            client.Notify(StratumConstants.MsgSetDifficulty, new object[] { context.Difficulty });

            // Send current job if available
            lock (currentJobParamsLock)
            {
                if (currentJobParams != null)
                    client.Notify(StratumConstants.MsgMiningNotify, currentJobParams);
            }
        }

        protected override async void OnClientAuthorize(StratumClient client, JsonRpcRequest request)
        {
            var context = GetMiningContext(client);

            var requestParams = request.Params?.ToObject<string[]>();
            var workername = requestParams?.Length > 0 ? requestParams[0] : null;
            var password = requestParams?.Length > 1 ? requestParams[1] : null;

            context.IsAuthorized = await manager.HandleWorkerAuthenticateAsync(client, workername, password);
            client.Respond(context.IsAuthorized, request.Id);
        }

        protected override async void OnClientSubmitShare(StratumClient client, JsonRpcRequest request)
        {
            var context = GetMiningContext(client);
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
		            var share = await manager.HandleWorkerSubmitShareAsync(client, requestParams, context.Difficulty);

		            client.Respond(true, request.Id);

					// TODO: record it

		            // update client stats
					context.Stats.ValidShares++;

					// telemetry
					validSharesSubject.OnNext(Unit.Default);
				}

				catch (StratumException ex)
	            {
		            client.RespondError(ex.Code, ex.Message, request.Id, false);

		            // update client stats
		            context.Stats.InvalidShares++;

		            // telemetry
		            invalidSharesSubject.OnNext(Unit.Default);

					// TODO: banning check
				}
			}
        }

        private PoolClientContext GetMiningContext(StratumClient client)
        {
            PoolClientContext context;

            lock (miningContexts)
            {
                if (!miningContexts.TryGetValue(client, out context))
                {
                    context = new PoolClientContext(client, poolConfig);
                    miningContexts.Add(client, context);
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
                        logger.Info(() => $"[{client.ConnectionId}] Booting client because communication was not established within the alloted time");

                        DisconnectClient(client);
                    }
                });
        }

        private void UpdateVarDiff(StratumClient client)
        {
            var context = GetMiningContext(client);

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

            lock (currentJobParamsLock)
            {
                currentJobParams = jobParams;
            }

            if (jobParams != null)
                BroadcastJob(jobParams);
        }

        private void BroadcastJob(object jobParams)
        {
            BroadcastNotification(StratumConstants.MsgMiningNotify, jobParams, client =>
            {
                var context = GetMiningContext(client);

                if (context.IsSubscribed)
                {
                    // if the client has a pending difficulty change, apply it now
                    if(context.ApplyPendingDifficulty())
                        client.Notify(StratumConstants.MsgSetDifficulty, new object[] { context.Difficulty });

	                client.Notify(StratumConstants.MsgMiningNotify, currentJobParams);
				}

				return false;
            });
        }

	    private void SetupTelemetry()
	    {
			// Average response times per minute
		    resposeTimesSubject
			    .Select(ms => (float) ms)
			    .Buffer(TimeSpan.FromMinutes(1))
			    .Select(responses => responses.Average())
			    .Subscribe(avg => poolStats.AverageResponseTimePerMinuteMs = avg);

		    // Shares per second
			Observable.Merge(validSharesSubject, invalidSharesSubject)
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
