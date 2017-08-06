using System;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using MiningForce.Blockchain.Monero.StratumRequests;
using MiningForce.Blockchain.Monero.StratumResponses;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using MiningForce.Mining;
using MiningForce.Persistence;
using MiningForce.Stratum;
using Newtonsoft.Json;

namespace MiningForce.Blockchain.Monero
{
	[CoinMetadata(CoinType.XMR)]
    public class MoneroPool : PoolBase<MoneroWorkerContext>
    {
	    public MoneroPool(IComponentContext ctx,
		    JsonSerializerSettings serializerSettings,
		    IConnectionFactory cf) : 
		    base(ctx, serializerSettings, cf)
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

		protected override async Task OnRequestAsync(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;

			switch (request.Method)
			{
				case MoneroStratumMethods.Login:
					OnLogin(client, tsRequest);
					break;

				case MoneroStratumMethods.GetJob:
					OnGetJob(client, tsRequest);
					break;

				case MoneroStratumMethods.Submit:
					await OnSubmitAsync(client, tsRequest);
					break;

				case MoneroStratumMethods.KeepAlive:
					// ignored
					break;

				default:
					logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

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

		    if (request.Id == null)
		    {
			    client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
			    return;
		    }

			var loginRequest = request.ParamsAs<MoneroLoginRequest>();

			// validate login
		    if (string.IsNullOrEmpty(loginRequest?.Login))
		    {
				client.RespondError(request.Id, -1, "missing login");
			    return;
		    }

			// assumes that StratumLoginRequest.Login is an address
		    var result = manager.ValidateAddress(loginRequest.Login);

			client.Context.IsSubscribed = result;
			client.Context.IsAuthorized = result;
		    client.Context.WorkerName = loginRequest.Login;

		    if (!client.Context.IsAuthorized)
		    {
			    client.RespondError(request.Id, -1, "invalid login");
			    return;
		    }

			// respond
			var loginResponse = new MoneroLoginResponse
		    {
			    Id = client.ConnectionId,
				Job = CreateWorkerJob(client),
		    };

		    client.Respond(loginResponse, request.Id);
		}

	    private void OnGetJob(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;

			if (request.Id == null)
		    {
			    client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
			    return;
		    }

			var getJobRequest = request.ParamsAs<MoneroGetJobRequest>();

		    // validate worker
		    if (client.ConnectionId != getJobRequest?.WorkerId || !client.Context.IsAuthorized)
		    {
			    client.RespondError(request.Id, -1, "unauthorized");
			    return;
		    }

			// respond
		    var job = CreateWorkerJob(client);
			client.Respond(job, request.Id);
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
		    }

			return result;
	    }

	    private async Task OnSubmitAsync(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;

			if (request.Id == null)
		    {
			    client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
			    return;
		    }

			// check age of submission (aged submissions are usually caused by high server load)
			var requestAge = DateTime.UtcNow - tsRequest.Timestamp;

		    if (requestAge > maxShareAge)
		    {
			    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
			    return;
		    }

			// check request
			var submitRequest = request.ParamsAs<MoneroSubmitShareRequest>();

			// validate worker
			if (client.ConnectionId != submitRequest?.WorkerId || !client.Context.IsAuthorized)
				throw new StratumException(StratumError.MinusOne, "unauthorized");

			// recognize activity
		    client.Context.LastActivity = DateTime.UtcNow;

			UpdateVarDiff(client, manager.BlockchainStats.NetworkDifficulty);

		    try
		    {
			    MoneroWorkerJob job;

			    lock (client.Context)
			    {
				    if (string.IsNullOrEmpty(submitRequest?.JobId) || !client.Context.ValidJobs.Any(x => x.Id == submitRequest.JobId))
					    throw new StratumException(StratumError.MinusOne, "invalid jobid");

					// look it up
					job = client.Context.ValidJobs.First(x => x.Id == submitRequest.JobId);
				}

				// dupe check
			    var nonceLower = submitRequest.Nonce.ToLower();

			    lock (job)
			    {
				    if (job.Submissions.Contains(nonceLower))
					    throw new StratumException(StratumError.MinusOne, "duplicate share");

					job.Submissions.Add(nonceLower);
				}

			    var share = await manager.SubmitShareAsync(client, submitRequest, job, client.Context.Difficulty);

				// success
			    client.Respond(new MoneroResponseBase(), request.Id);

			    // record it
			    shareSubject.OnNext(share);

			    // update pool stats
			    if (share.IsBlockCandidate)
				    poolStats.LastPoolBlockTime = DateTime.UtcNow;

			    // update client stats
			    client.Context.Stats.ValidShares++;

			    // telemetry
			    validSharesSubject.OnNext(share);
		    }

		    catch (StratumException ex)
		    {
			    client.RespondError(ex.Code, ex.Message, request.Id, false);

			    // update client stats
			    client.Context.Stats.InvalidShares++;

			    // telemetry
			    invalidSharesSubject.OnNext(Unit.Default);

			    // banning
			    if (poolConfig.Banning?.Enabled == true)
				    ConsiderBan(client, client.Context, poolConfig.Banning);
		    }
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
