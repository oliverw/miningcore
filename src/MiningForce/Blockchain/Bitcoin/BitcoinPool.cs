using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Autofac;
using MiningForce.JsonRpc;
using MiningForce.Mining;
using MiningForce.Persistence;
using MiningForce.Persistence.Repositories;
using MiningForce.Stratum;
using Newtonsoft.Json;
using NLog;

namespace MiningForce.Blockchain.Bitcoin
{
	[BitcoinCoinsMetaData]
    public class BitcoinPool : PoolBase<BitcoinWorkerContext>
    {
	    public BitcoinPool(IComponentContext ctx,
		    JsonSerializerSettings serializerSettings,
		    IConnectionFactory cf,
		    IStatsRepository statsRepo) : 
		    base(ctx, serializerSettings, cf, statsRepo)
	    {
	    }

	    private object currentJobParams;
	    private BitcoinJobManager manager;
	    private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);

		#region Overrides

		protected override async Task InitializeJobManager()
	    {
		    manager = ctx.Resolve<BitcoinJobManager>();
		    manager.Configure(poolConfig, clusterConfig);

			await manager.StartAsync(this);
		    manager.Jobs.Subscribe(OnNewJob);

		    // we need work before opening the gates
		    await manager.Jobs.Take(1).ToTask();
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

	    protected override void UpdateBlockChainStats()
	    {
		    blockchainStats = manager.BlockchainStats;
	    }

	    #endregion // Overrides

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
		    var context = client.ContextAs<BitcoinWorkerContext>();
		    context.IsSubscribed = true;
		    context.UserAgent = requestParams?.Length > 0 ? requestParams[0] : null;

		    // send intial update
		    client.Notify(StratumMethod.SetDifficulty, new object[] { context.Difficulty });
		    client.Notify(StratumMethod.MiningNotify, currentJobParams);
	    }

	    private async void OnAuthorize(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;
		    var context = client.ContextAs<BitcoinWorkerContext>();

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
			    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
			    return;
		    }

		    // check worker state
		    var request = tsRequest.Value;
		    var context = client.ContextAs<BitcoinWorkerContext>();
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

	    private void OnNewJob(object jobParams)
	    {
		    currentJobParams = jobParams;

		    BroadcastNotification(StratumMethod.MiningNotify, currentJobParams, client =>
		    {
			    var context = client.ContextAs<BitcoinWorkerContext>();

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

						    client.Notify(StratumMethod.SetDifficulty, new object[] { context.Difficulty });
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
	}
}
