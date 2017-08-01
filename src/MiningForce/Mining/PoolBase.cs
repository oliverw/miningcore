using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningForce.Banning;
using MiningForce.Blockchain;
using MiningForce.Blockchain.Bitcoin;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using MiningForce.Stratum;
using MiningForce.Util;
using MiningForce.VarDiff;
using Newtonsoft.Json;
using NLog;

namespace MiningForce.Mining
{
    public abstract class PoolBase<TWorkerContext> : StratumServer,
	    IMiningPool
		where TWorkerContext : WorkerContextBase, new()
    {
	    protected PoolBase(IComponentContext ctx,
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

	    protected PoolConfig poolConfig;
	    protected ClusterConfig clusterConfig;
	    protected readonly JsonSerializerSettings serializerSettings;
	    protected readonly IConnectionFactory cf;
	    protected readonly IStatsRepository statsRepo;
	    protected readonly PoolStats poolStats = new PoolStats();
	    protected BlockchainStats blockchainStats;
		protected IBanManager banManager;

		protected readonly Dictionary<PoolEndpoint, VarDiffManager> varDiffManagers = 
            new Dictionary<PoolEndpoint, VarDiffManager>();

        private static readonly string[] HashRateUnits = { " KH", " MH", " GH", " TH", " PH" };

	    protected readonly Subject<IShare> shareSubject = new Subject<IShare>();

		// Telemetry
	    protected readonly Subject<int> resposeTimesSubject = new Subject<int>();
	    protected readonly Subject<IShare> validSharesSubject = new Subject<IShare>();
	    protected readonly Subject<Unit> invalidSharesSubject = new Subject<Unit>();

	    #region API-Surface

		public IObservable<IShare> Shares { get; }

	    public virtual void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
	    {
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
		    Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

		    this.logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinJobManager), poolConfig);
			this.poolConfig = poolConfig;
		    this.clusterConfig = clusterConfig;
	    }

		public virtual async Task StartAsync()
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

	    protected abstract Task InitializeJobManager();

	    protected override string LogCat => "Pool";

	    protected override void OnConnect(StratumClient client)
        {
	        if (banManager?.IsBanned(client.RemoteEndpoint.Address) == false)
	        {
				// client setup
		        var context = new TWorkerContext();
				context.Init(client, poolConfig);
				client.Context = context;

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

        protected void UpdateVarDiff(StratumClient client, double networkDifficulty)
        {
            var context = client.ContextAs<TWorkerContext>();

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

	    protected void SetupBanning(ClusterConfig clusterConfig)
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
				.Do(_=> UpdateBlockChainStats())
			    .Subscribe(_ => PersistStats());
	    }

	    protected abstract void UpdateBlockChainStats();

		private void PersistStats()
	    {
		    try
		    {
				logger.Debug(()=> $"[{LogCat}] Persisting stats");

			    cf.RunTx((con, tx) =>
			    {
				    statsRepo.UpdatePoolStats(con, tx, poolConfig.Id, poolStats, blockchainStats);
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

	    protected void ConsiderBan(StratumClient client, WorkerContextBase context, PoolBanningConfig config)
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
Network Connected:      {blockchainStats.NetworkType}
Detected Reward Type:   {blockchainStats.RewardType}
Current Block Height:   {blockchainStats.BlockHeight}
Current Connect Peers:  {blockchainStats.ConnectedPeers}
Network Difficulty:     {blockchainStats.NetworkDifficulty}
Network Hash Rate:      {FormatHashRate(blockchainStats.NetworkHashRate)}
Stratum Port(s):        {string.Join(", ", poolConfig.Ports.Keys)}
Pool Fee:               {poolConfig.RewardRecipients.Sum(x => x.Percentage)}%
";

            logger.Info(()=> msg);
        }
    }
}
