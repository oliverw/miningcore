/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Banning;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using MiningCore.VarDiff;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Mining
{
    public abstract class PoolBase<TShare> : StratumServer,
        IMiningPool
		where TShare: IShare
    {
        protected PoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) : base(ctx, clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));

            this.serializerSettings = serializerSettings;
            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.notificationService = notificationService;

            Shares = shareSubject
                .Synchronize();
        }

        protected readonly PoolStats poolStats = new PoolStats();
        protected readonly JsonSerializerSettings serializerSettings;
        protected readonly NotificationService notificationService;
        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        protected readonly IMapper mapper;
        protected readonly CompositeDisposable disposables = new CompositeDisposable();
        protected BlockchainStats blockchainStats;
        protected PoolConfig poolConfig;
        protected const int VarDiffSampleCount = 32;
        protected static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(6);
        protected readonly Subject<ClientShare> shareSubject = new Subject<ClientShare>();

        protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers =
            new Dictionary<PoolEndpoint, VarDiffManager>();

        protected override string LogCat => "Pool";

        protected abstract Task SetupJobManager();
        protected abstract WorkerContextBase CreateClientContext();

        protected override void OnConnect(StratumClient client)
        {
            // update stats
            lock(clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }

            // client setup
            var context = CreateClientContext();

            var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];
            context.Init(poolConfig, poolEndpoint.Difficulty, !poolConfig.ExternalStratum ? poolEndpoint.VarDiff : null, clock);
            client.SetContext(context);

            // varDiff setup
            if (context.VarDiff != null)
            {
                // get or create manager
                lock(varDiffManagers)
                {
                    if (!varDiffManagers.TryGetValue(poolEndpoint, out var varDiffManager))
                    {
                        varDiffManager = new VarDiffManager(poolEndpoint.VarDiff, clock);
                        varDiffManagers[poolEndpoint] = varDiffManager;
                    }
                }

                // wire updates
                lock(context.VarDiff)
                {
                    context.VarDiff.Subscription = Shares
                        .Where(x => x.Client == client)
                        .Timestamp()
                        .Select(x => x.Timestamp.ToUnixTimeMilliseconds())
                        .Buffer(TimeSpan.FromSeconds(poolEndpoint.VarDiff.RetargetTime), VarDiffSampleCount)
                        .Subscribe(timestamps =>
                        {
                            try
                            {
                                VarDiffManager varDiffManager;

                                lock(varDiffManagers)
                                {
                                    varDiffManager = varDiffManagers[poolEndpoint];
                                }

                                var newDiff = varDiffManager.Update(context, timestamps, client.ConnectionId, logger);

                                if (newDiff.HasValue)
                                {
                                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {Math.Round(newDiff.Value, 2)}");

                                    OnVarDiffUpdate(client, newDiff.Value);
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex);
                            }
                        });
                }
            }

            // expect miner to establish communication within a certain time
            EnsureNoZombieClient(client);
        }

        protected override void DisconnectClient(StratumClient client)
        {
            var context = client.GetContextAs<WorkerContextBase>();

            if (context.VarDiff != null)
            {
                lock(context.VarDiff)
                {
                    context.VarDiff.Dispose();
                }
            }

            base.DisconnectClient(client);
        }

        protected override void OnDisconnect(string subscriptionId)
        {
            // update stats
            lock(clients)
            {
                poolStats.ConnectedMiners = clients.Count;
            }
        }

        private void EnsureNoZombieClient(StratumClient client)
        {
            Observable.Timer(clock.Now.AddSeconds(10))
                .Take(1)
                .Subscribe(_ =>
                {
                    if (!client.LastReceive.HasValue)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (post-connect silence)");

                        DisconnectClient(client);
                    }
                });
        }

	    private void StartExternalStratumPublisherListener()
	    {
		    var thread = new Thread(() =>
			{
				var serializer = new JsonSerializer
				{
					ContractResolver = new CamelCasePropertyNamesContractResolver()
				};

				while(true)
				{
					try
					{
						using (var subSocket = new SubscriberSocket())
						{
							//subSocket.Options.ReceiveHighWatermark = 1000;
							subSocket.Connect(poolConfig.ExternalStratumZmqSocket);
							subSocket.Subscribe(poolConfig.ExternalStratumZmqTopic);

							logger.Info($"Monitoring external stratum {poolConfig.ExternalStratumZmqSocket}/{poolConfig.ExternalStratumZmqTopic}");

							while (true)
							{
								var msg = subSocket.ReceiveMultipartMessage(2);
								var topic = msg.First().ConvertToString(Encoding.UTF8);
								var data = msg.Last().ConvertToString(Encoding.UTF8);

								// validate
								if (topic != poolConfig.ExternalStratumZmqTopic)
								{
									logger.Warn(()=> $"Received non-matching topic {topic} on ZeroMQ subscriber socket");
									continue;
								}

								if(string.IsNullOrEmpty(data))
								{
									logger.Warn(() => $"Received empty data on ZeroMQ subscriber socket");
									continue;
								}

								// deserialize
								TShare share;

								using (var reader = new StringReader(data))
								{
									using (var jreader = new JsonTextReader(reader))
									{
										share = serializer.Deserialize<TShare>(jreader);
									}
								}

								if (share == null)
								{
									logger.Error(() => "Unable to deserialize share received from ZeroMQ subscriber socket");
									continue;
								}

								// fill in the blacks
								share.PoolId = poolConfig.Id;
								share.Created = clock.Now;

								// re-publish
								shareSubject.OnNext(new ClientShare(null, share));

								logger.Info(() => $"[{LogCat}] External share accepted: D={Math.Round(share.Difficulty, 3)}");
							}
						}
					}

					catch (Exception ex)
					{
						logger.Error(ex);
					}
				}
			});

		    thread.Name = $"{poolConfig.Id} external stratum listener";
			thread.Start();
	    }

		#region VarDiff

		protected virtual void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<WorkerContextBase>();
            context.EnqueueNewDifficulty(newDiff);
        }

        #endregion // VarDiff

        protected void SetupBanning(ClusterConfig clusterConfig)
        {
            if (poolConfig.Banning?.Enabled == true)
            {
                var managerType = clusterConfig.Banning?.Manager ?? BanManagerKind.Integrated;
                banManager = ctx.ResolveKeyed<IBanManager>(managerType);
            }
        }

        protected virtual void SetupStats()
        {
            LoadStats();

            // Periodically persist pool- and blockchain-stats to persistent storage
            disposables.Add(Observable.Interval(TimeSpan.FromSeconds(60))
                .Select(_ => Observable.FromAsync(async () =>
                {
                    try
                    {
                        await UpdateBlockChainStatsAsync();
                    }
                    catch(Exception)
                    {
                        // ignored
                    }
                }))
                .Concat()
                .Subscribe(_ => PersistStats()));

            // For external stratums, miner counts are derived from submitted shares
            if (poolConfig.ExternalStratum)
            {
                disposables.Add(Shares
                    .Buffer(TimeSpan.FromMinutes(1))
                    .Do(shares =>
                    {
                        var sharesByMiner = shares.GroupBy(x => x.Share.Miner).ToArray();
                        poolStats.ConnectedMiners = sharesByMiner.Length;
                    })
                    .Subscribe());

            }
        }

        protected abstract Task UpdateBlockChainStatsAsync();

        private void LoadStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Loading pool stats");

                var stats = cf.Run(con => statsRepo.GetLastPoolStats(con, poolConfig.Id));

                if (stats != null)
                {
                    poolStats.ConnectedMiners = stats.ConnectedMiners;
                    poolStats.PoolHashRate = (ulong) stats.PoolHashRate;
                }
            }

            catch (Exception ex)
            {
                logger.Warn(ex, () => $"[{LogCat}] Unable to load pool stats");
            }
        }

        private void PersistStats()
        {
            try
            {
                logger.Debug(() => $"[{LogCat}] Persisting pool stats");

                cf.RunTx((con, tx) =>
                {
                    var mapped = mapper.Map<Persistence.Model.PoolStats>(poolStats);
                    mapped.PoolId = poolConfig.Id;
                    mapped.Created = clock.Now;

                    statsRepo.InsertPoolStats(con, tx, mapped);
                });
            }

            catch(Exception ex)
            {
                logger.Error(ex, () => $"[{LogCat}] Unable to persist pool stats");
            }
        }

        protected void ConsiderBan(StratumClient client, WorkerContextBase context, PoolShareBasedBanningConfig config)
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
                    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Banning worker for {config.Time} sec: {Math.Floor(ratioBad * 100)}% of the last {totalShares} shares were invalid");

                    banManager.Ban(client.RemoteEndpoint.Address, TimeSpan.FromSeconds(config.Time));

                    DisconnectClient(client);
                }
            }
        }

        private IPEndPoint PoolEndpoint2IPEndpoint(int port, PoolEndpoint pep)
        {
            var listenAddress = IPAddress.Parse("127.0.0.1");
            if (!string.IsNullOrEmpty(pep.ListenAddress))
                listenAddress = pep.ListenAddress != "*" ? IPAddress.Parse(pep.ListenAddress) : IPAddress.Any;

            return new IPEndPoint(listenAddress, port);
        }

        private void OutputPoolInfo()
        {
            var msg = $@"

Mining Pool:            {poolConfig.Id}
Coin Type:              {poolConfig.Coin.Type}
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatUtil.FormatHashRate(blockchainStats.NetworkHashRate)}
Stratum Port(s):        {string.Join(", ", poolConfig.Ports.Keys)}
Pool Fee:               {poolConfig.RewardRecipients.Sum(x => x.Percentage)}%
";

            logger.Info(() => msg);
        }

        protected abstract ulong HashrateFromShares(IEnumerable<ClientShare> shares, int interval);

        protected virtual void UpdateMinerHashrates(IList<ClientShare> shares, int interval)
        {
            try
            {
                var sharesByMiner = shares.GroupBy(x => x.Share.Miner).ToArray();

                foreach (var minerShares in sharesByMiner)
                {
                    // Total hashrate
                    var miner = minerShares.Key;
                    var hashRate = HashrateFromShares(minerShares, interval);

                    var sample = new MinerHashrateSample
                    {
                        PoolId = poolConfig.Id,
                        Miner = miner,
                        Hashrate = hashRate,
                        Created = clock.Now
                    };

                    // Per worker hashrates
                    var sharesPerWorker = minerShares
                        .GroupBy(x => x.Share.Worker)
                        .Where(x => !string.IsNullOrEmpty(x.Key));

                    foreach(var workerShares in sharesPerWorker)
                    {
                        var worker = workerShares.Key;
                        hashRate = HashrateFromShares(workerShares, interval);

                        if (sample.WorkerHashrates == null)
                            sample.WorkerHashrates = new Dictionary<string, ulong>();

                        sample.WorkerHashrates[worker] = hashRate;
                    }

                    // Persist
                    cf.RunTx((con, tx) => { statsRepo.RecordMinerHashrateSample(con, tx, sample); });
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }

        #region API-Surface

        public IObservable<ClientShare> Shares { get; }
        public PoolConfig Config => poolConfig;
        public PoolStats PoolStats => poolStats;
        public BlockchainStats NetworkStats => blockchainStats;

        public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger = LogUtil.GetPoolScopedLogger(typeof(PoolBase<TShare>), poolConfig);
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
        }

        public virtual async Task StartAsync()
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

            logger.Info(() => $"[{LogCat}] Launching ...");

            try
            {
	            SetupBanning(clusterConfig);
	            await SetupJobManager();

	            if (!poolConfig.ExternalStratum)
	            {
		            var ipEndpoints = poolConfig.Ports.Keys
			            .Select(port => PoolEndpoint2IPEndpoint(port, poolConfig.Ports[port]))
			            .ToArray();

		            StartListeners(ipEndpoints);
	            }

	            else
	            {
		            if (string.IsNullOrEmpty(poolConfig.ExternalStratumZmqSocket))
						logger.ThrowLogPoolStartupException($"[{LogCat}] Requested external stratum but no publisher socket specified", LogCat);
					else if (string.IsNullOrEmpty(poolConfig.ExternalStratumZmqTopic))
			            logger.ThrowLogPoolStartupException($"[{LogCat}] Requested external stratum but no publisher topic specified", LogCat);

					StartExternalStratumPublisherListener();
	            }

	            SetupStats();
                await UpdateBlockChainStatsAsync();

                logger.Info(() => $"[{LogCat}] Online");
                OutputPoolInfo();
            }

            catch(PoolStartupAbortException)
            {
                // just forward these
                throw;
            }

            catch(Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

	    #endregion // API-Surface
    }
}
