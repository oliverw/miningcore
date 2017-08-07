using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using CodeContracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MiningForce.Api.Responses;
using MiningForce.Configuration;
using MiningForce.Mining;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;

namespace MiningForce.Api
{
    public class ApiServer
	{
		public ApiServer(IComponentContext ctx, IMapper mapper)
		{
			this.mapper = mapper;
		}

		private readonly List<IMiningPool> pools = new List<IMiningPool>();
		private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

		private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Ignore
		};

		private readonly IMapper mapper;
		private IWebHost webHost;

		#region API-Surface

		public void Start(ClusterConfig clusterConfig)
		{
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			
			logger.Info(() => $"Launching ...");

			var address = clusterConfig.Api?.Address != null ?
				(clusterConfig.Api.Address != "*" ? IPAddress.Parse(clusterConfig.Api.Address) : IPAddress.Any) :
				IPAddress.Parse("127.0.0.1");

			var port = clusterConfig.Api?.Port ?? 4000;

			webHost = new WebHostBuilder()
				.Configure(app =>
				{
					app.Run(HandleRequest);
				})
				.UseKestrel(options =>
				{
					options.Listen(address, port);
				})
				.Build();

			webHost.Start();

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

		private async Task SendJson(HttpContext context, object response)
		{
			context.Response.ContentType = "application/json";

			var json = JsonConvert.SerializeObject(response, serializerSettings);
			await context.Response.WriteAsync(json, Encoding.UTF8);
		}

		private async Task HandleRequest(HttpContext context)
		{
			var request = context.Request;

			try
			{
				switch (request.Path)
				{
					case "/api/pools":
						await GetPools(context);
						break;

					default:
						context.Response.StatusCode = 404;
						context.Response.Body.Close();
						break;
				}
			}

			catch (Exception ex)
			{
				logger.Error(ex);

				context.Response.StatusCode = 500;
				context.Response.Body.Close();
			}
		}

		private async Task GetPools(HttpContext context)
		{
			GetPoolsResponse response;

			lock (pools)
			{
				response = new GetPoolsResponse
				{
					Pools = pools.Select(pool =>
					{
						var poolInfo = mapper.Map<PoolInfo>(pool.Config);

						poolInfo.PoolStats = pool.PoolStats;
						poolInfo.NetworkStats = pool.NetworkStats;

						return poolInfo;
					}).ToArray()
				};
			}

			await SendJson(context, response);
		}
	}
}
