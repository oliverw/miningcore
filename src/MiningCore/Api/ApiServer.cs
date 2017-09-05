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
using System.Linq;
using System.Net;
using System.Reactive;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MiningCore.Api.Responses;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Mining;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Api
{
    public class ApiServer
    {
        public ApiServer(
            IMapper mapper,
            IConnectionFactory cf,
            IStatsRepository statsRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));

            this.cf = cf;
            this.statsRepo = statsRepo;
            this.mapper = mapper;

            requestMap = new Dictionary<Regex, Func<HttpContext, Match, Task>>
            {
                {new Regex("^/api/pools$", RegexOptions.Compiled), HandleGetPoolsAsync},
                {new Regex("^/api/pool/(?<poolId>[^/]+)/stats$", RegexOptions.Compiled), HandleGetPoolStatsAsync}
            };
        }

        protected readonly IConnectionFactory cf;
        protected readonly IStatsRepository statsRepo;
        private readonly IMapper mapper;

        private readonly List<IMiningPool> pools = new List<IMiningPool>();
        private IWebHost webHost;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Dictionary<Regex, Func<HttpContext, Match, Task>> requestMap;

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
                foreach (var path in requestMap.Keys)
                {
                    var m = path.Match(request.Path);

                    if (m.Success)
                    {
                        var handler = requestMap[path];
                        await handler(context, m);
                        return;
                    }
                }

                context.Response.StatusCode = 404;
            }

            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        private async Task HandleGetPoolsAsync(HttpContext context, Match m)
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

        private async Task HandleGetPoolStatsAsync(HttpContext context, Match m)
        {
            var poolId = m.Groups["poolId"].Value;

            if (string.IsNullOrEmpty(poolId))
            {
                context.Response.StatusCode = 404;
                return;
            }

            lock (pools)
            {
                if(!pools.Any(x => x.Config.Id == poolId))
                {
                    context.Response.StatusCode = 404;
                    return;
                }
            }

            var stats = cf.Run(con => statsRepo.PageStatsBetween(
                con, poolId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 0, 20));

            await SendJson(context, stats);
        }

        #region API-Surface

        public void Start(ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));

            logger.Info(() => $"Launching ...");

            var address = clusterConfig.Api?.ListenAddress != null
                ? (clusterConfig.Api.ListenAddress != "*" ? IPAddress.Parse(clusterConfig.Api.ListenAddress) : IPAddress.Any)
                : IPAddress.Parse("127.0.0.1");

            var port = clusterConfig.Api?.Port ?? 4000;

            webHost = new WebHostBuilder()
                .Configure(app => { app.Run(HandleRequest); })
                .UseKestrel(options => { options.Listen(address, port); })
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
    }
}
