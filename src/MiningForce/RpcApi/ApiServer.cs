using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using MiningForce.Configuration;
using MiningForce.JsonRpc;
using MiningForce.Mining;
using MiningForce.RpcApi.ApiResponses;
using MiningForce.Stratum;
using NLog;

namespace MiningForce.RpcApi
{
    public class ApiServer : StratumServer<Unit>
	{
		public ApiServer(IComponentContext ctx) : base(ctx)
		{
			logger = LogManager.GetCurrentClassLogger();
		}

		protected override string LogCat => "API";
		private ClusterConfig clusterConfig;
		private readonly List<IMiningPool> pools = new List<IMiningPool>();

		#region API-Surface

		public void Start(ClusterConfig clusterConfig)
		{
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			
			logger.Info(() => $"Launching ...");

			this.clusterConfig = clusterConfig;

			var address = clusterConfig.Api?.Address != null ?
				(clusterConfig.Api.Address != "*" ? IPAddress.Parse(clusterConfig.Api.Address) : IPAddress.Any) :
				IPAddress.Parse("127.0.0.1");

			var port = clusterConfig.Api?.Port ?? 4000;

			StartListeners(new []{ new IPEndPoint(address, port) });

			logger.Info(() => $"Online @ {address}:{port}");
		}

		public void AttachPool(IMiningPool pool)
		{
			lock (pools)
			{
				pools.Add(pool);
			}
		}

		#endregion // API-Surface

		protected override void OnConnect(StratumClient<Unit> client)
		{
		}

		protected override void OnDisconnect(string subscriptionId)
		{
		}

		protected override Task OnRequestAsync(StratumClient<Unit> client, Timestamped<JsonRpcRequest> tsRequest)
		{
			var request = tsRequest.Value;

			if (request.Id == null)
			{
				client.RespondError(StratumError.Other, "missing request id", request.Id);
				return Task.FromResult(false);
			}

			switch (request.Method)
			{
				case ApiMethods.GetClusterConfig:
					GetClusterConfig(client, request, tsRequest);
					break;

				case ApiMethods.GetPools:
					GetPools(client, request, tsRequest);
					break;

				default:
					client.RespondError(StratumError.Other, "unsupported method", request.Id);
					break;
			}

			return Task.FromResult(false);
		}

		private void GetClusterConfig(StratumClient<Unit> client, JsonRpcRequest request, Timestamped<JsonRpcRequest> tsRequest)
		{
			client.Respond(clusterConfig, request.Id);
		}

		private void GetPools(StratumClient<Unit> client, JsonRpcRequest request, Timestamped<JsonRpcRequest> tsRequest)
		{
			GetPoolsResponse response;

			lock (pools)
			{
				response = new GetPoolsResponse
				{
					Pools = pools.Select(pool => new PoolInfo
					{
						Id = pool.Config.Id,
						Config = pool.Config,
						PoolStats = pool.PoolStats,
						NetworkStats = pool.NetworkStats
					}).ToArray()
				};
			}

			client.Respond(response, request.Id);
		}
	}
}
