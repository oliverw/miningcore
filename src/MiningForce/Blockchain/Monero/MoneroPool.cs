using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningForce.Blockchain.Bitcoin;
using MiningForce.Blockchain.Monero.StratumRequests;
using MiningForce.Blockchain.Monero.StratumResponses;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using MiningForce.Mining;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using MiningForce.Stratum;
using Newtonsoft.Json;
using NLog;

namespace MiningForce.Blockchain.Monero
{
	[CoinMetadata(CoinType.XMR)]
    public class MoneroPool : PoolBase<MoneroWorkerContext>
    {
	    public MoneroPool(IComponentContext ctx,
		    JsonSerializerSettings serializerSettings,
		    IConnectionFactory cf,
		    IStatsRepository statsRepo) : 
		    base(ctx, serializerSettings, cf, statsRepo)
	    {
	    }

	    private MoneroJobManager manager;
	    private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);
	    private long jobId;

		#region Overrides

		protected override async Task InitializeJobManager()
	    {
		    manager = ctx.Resolve<MoneroJobManager>();
		    manager.Configure(poolConfig, clusterConfig);

			await manager.StartAsync();

		    manager.Blocks.Subscribe(_=> OnNewJob());

		    // we need work before opening the gates
		    await manager.Blocks.Take(1).ToTask();
		}

		protected override void OnRequest(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;

			switch (request.Method)
			{
				case MoneroStratumMethods.Login:
					OnLogin(client, tsRequest);
					break;

				default:
					logger.Warn(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

					client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
					break;
			}
	    }

	    protected override void UpdateBlockChainStats()
	    {
		    blockchainStats = manager.BlockchainStats;
	    }

	    #endregion // Overrides

		private void OnLogin(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;
			var loginRequest = request.Params?.ToObject<StratumLoginRequest>();

			// validate login
		    if (string.IsNullOrEmpty(loginRequest?.Login))
		    {
				client.RespondError(request.Id, -1, "missing login");
			    return;
		    }

			// assumes that StratumLoginRequest.Login is an address
			client.Context.IsAuthorized = manager.ValidateAddress(loginRequest.Login);

		    if (!client.Context.IsAuthorized)
		    {
			    client.RespondError(request.Id, -1, "invalid login");
			    return;
		    }

			// respond
			var loginResponse = new LoginResponse
		    {
			    Id = client.ConnectionId,
				Job = CreateWorkerJob(client),
		    };

		    client.Respond(loginResponse, request.Id);
		}

	    private MoneroJobParams CreateWorkerJob(StratumClient<MoneroWorkerContext> client)
	    {
		    var job = new MoneroWorkerJob(NextJobId(), client.Context.Difficulty);

		    string blob, target;
			manager.PrepareWorkerJob(job, out blob, out target);

			// should never happen
		    if (string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(blob))
			    return null;

		    var result = new MoneroJobParams
		    {
			    JobId = job.Id,
			    Blob = blob,
				Target = target
		    };

		    // update context
		    lock (client.Context)
		    {
			    client.Context.AddJob(job);
			    client.Context.LastQueryBlockHeight = job.Height;
		    }

			return result;
	    }

		private string NextJobId()
	    {
		    return Interlocked.Increment(ref jobId).ToString(CultureInfo.InvariantCulture);
	    }

		private void OnNewJob()
	    {
		    ForEachClient(client =>
		    {
			    if (client.Context.IsSubscribed)
			    {
				    // check if turned zombie
				    var lastActivityAgo = DateTime.UtcNow - client.Context.LastActivity;

				    if (poolConfig.ClientConnectionTimeout == 0 ||
				        lastActivityAgo.TotalSeconds < poolConfig.ClientConnectionTimeout)
				    {
					    // varDiff: if the client has a pending difficulty change, apply it now
					    if (client.Context.ApplyPendingDifficulty())
						    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] VarDiff update to {client.Context.Difficulty}");

					    // send job
					    var job = CreateWorkerJob(client);
						client.Notify(MoneroStratumMethods.JobNotify, job);
				    }

				    else
				    {
					    logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");

					    DisconnectClient(client);
				    }
			    }
		    });
	    }
	}
}
