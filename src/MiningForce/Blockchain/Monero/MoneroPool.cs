using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Autofac;
using MiningForce.Blockchain.Bitcoin;
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
	[SupportedCoinsMetadata(CoinType.XMR)]
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

		#region Overrides

		protected override async Task InitializeJobManager()
	    {
		    manager = ctx.Resolve<MoneroJobManager>();
		    manager.Configure(poolConfig, clusterConfig);

			await manager.StartAsync();
	    }

	    protected override void OnRequest(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;
		    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Received request {request.Method} [{request.Id}]: {JsonConvert.SerializeObject(request.Params, serializerSettings)}");

		    try
		    {
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

		private void OnLogin(StratumClient<MoneroWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
	    {
		    var request = tsRequest.Value;
		    var requestParams = request.Params?.ToObject<string[]>();

		 //   var data = new object[]
			//{
			//	new object[]
			//	{
			//		new object[] { MoneroStratumMethods.SetDifficulty, client.ConnectionId },
			//		new object[] { MoneroStratumMethods.MiningNotify, client.ConnectionId }
			//	},
			//}
			//.Concat(manager.GetSubscriberData(client))
			//.ToArray();

		 //   client.Respond(data, request.Id);
	    }
	}
}
