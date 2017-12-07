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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using MiningCore.Api.Responses;
using MiningCore.Blockchain;
using MiningCore.Buffers;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Mining;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
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
            IBlockRepository blocksRepo,
            IPaymentRepository paymentsRepo,
            IStatsRepository statsRepo,
            IMasterClock clock)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(blocksRepo, nameof(blocksRepo));
            Contract.RequiresNonNull(paymentsRepo, nameof(paymentsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.cf = cf;
            this.statsRepo = statsRepo;
            this.blocksRepo = blocksRepo;
            this.paymentsRepo = paymentsRepo;
            this.mapper = mapper;
            this.clock = clock;

            requestMap = new Dictionary<Regex, Func<HttpContext, Match, Task>>
            {
                { new Regex("^/api/pools$", RegexOptions.Compiled), HandleGetPoolsAsync },
                { new Regex("^/api/pool/(?<poolId>[^/]+)/stats/hourly$", RegexOptions.Compiled), HandleGetPoolStatsAsync },
                { new Regex("^/api/pool/(?<poolId>[^/]+)/blocks$", RegexOptions.Compiled), HandleGetBlocksPagedAsync },
                { new Regex("^/api/pool/(?<poolId>[^/]+)/payments$", RegexOptions.Compiled), HandleGetPaymentsPagedAsync },
                { new Regex("^/api/pool/(?<poolId>[^/]+)/miner/(?<address>[^/]+)/stats$", RegexOptions.Compiled), HandleGetMinerStatsAsync },

                // admin api
                { new Regex("^/api/admin/forcegc$", RegexOptions.Compiled), HandleForceGcAsync },
                { new Regex("^/api/admin/stats/gc$", RegexOptions.Compiled), HandleGcStatsAsync },
            };
        }

        private readonly IConnectionFactory cf;
        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IMapper mapper;
        private readonly IMasterClock clock;

        private readonly List<IMiningPool> pools = new List<IMiningPool>();
        private IWebHost webHost;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializer serializer = new JsonSerializer
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Dictionary<Regex, Func<HttpContext, Match, Task>> requestMap;

        private async Task SendJson(HttpContext context, object response)
        {
            context.Response.ContentType = "application/json";

            // add CORS headers
            context.Response.Headers.Add("Access-Control-Allow-Origin", new StringValues("*"));
            context.Response.Headers.Add("Access-Control-Allow-Methods", new StringValues("GET, POST, DELETE, PUT, OPTIONS, HEAD"));

            using (var stream = context.Response.Body)
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    serializer.Serialize(writer, response);

                    // append newline
                    await writer.WriteLineAsync();
                    await writer.FlushAsync();
                }
            }
        }

        private async Task HandleRequest(HttpContext context)
        {
            var request = context.Request;

            try
            {
                logger.Debug(() => $"Processing request {request.GetEncodedPathAndQuery()}");

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

            catch(Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        private IMiningPool GetPool(HttpContext context, Match m)
        {
            var poolId = m.Groups["poolId"]?.Value;

            if (!string.IsNullOrEmpty(poolId))
            {
                lock(pools)
                {
                    var pool = pools.FirstOrDefault(x => x.Config.Id == poolId);

                    if (pool != null)
                        return pool;
                }
            }

            context.Response.StatusCode = 404;
            return null;
        }

        private async Task HandleGetPoolsAsync(HttpContext context, Match m)
        {
            GetPoolsResponse response;

            lock(pools)
            {
                response = new GetPoolsResponse
                {
                    Pools = pools.Select(pool =>
                    {
                        var poolInfo = mapper.Map<PoolInfo>(pool.Config);

                        poolInfo.PoolStats = pool.PoolStats;
                        poolInfo.NetworkStats = pool.NetworkStats;

                        // pool wallet link
                        CoinMetaData.AddressInfoLinks.TryGetValue(pool.Config.Coin.Type, out var addressInfobaseUrl);
                        if (!string.IsNullOrEmpty(addressInfobaseUrl))
                            poolInfo.AddressInfoLink = string.Format(addressInfobaseUrl, poolInfo.Address);

                        // pool fees
                        poolInfo.PoolFeePercent = (float) pool.Config.RewardRecipients
                            .Sum(x => x.Percentage);

                        return poolInfo;
                    }).ToArray()
                };
            }

            await SendJson(context, response);
        }

        private async Task HandleGetPoolStatsAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            // set range
            var end = clock.Now;
            var start = end.AddDays(-1);

            var stats = cf.Run(con => statsRepo.GetPoolStatsBetweenHourly(
                con, pool.Config.Id, start, end));

            var response = new GetPoolStatsResponse
            {
                Stats = stats.Select(mapper.Map<AggregatedPoolStats>).ToArray()
            };

            await SendJson(context, response);
        }

        private async Task HandleGetBlocksPagedAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var blocks = cf.Run(con => blocksRepo.PageBlocks(con, pool.Config.Id,
                    new[] { BlockStatus.Confirmed, BlockStatus.Pending }, page, pageSize))
                .Select(mapper.Map<Responses.Block>)
                .ToArray();

            // enrich blocks
            CoinMetaData.BlockInfoLinks.TryGetValue(pool.Config.Coin.Type, out var blockInfobaseUrl);

            foreach(var block in blocks)
            {
                // compute infoLink
                if (!string.IsNullOrEmpty(blockInfobaseUrl))
                    block.InfoLink = string.Format(blockInfobaseUrl, block.BlockHeight);
            }

            await SendJson(context, blocks);
        }

        private async Task HandleGetPaymentsPagedAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var page = context.GetQueryParameter<int>("page", 0);
            var pageSize = context.GetQueryParameter<int>("pageSize", 20);

            if (pageSize == 0)
            {
                context.Response.StatusCode = 500;
                return;
            }

            var payments = cf.Run(con => paymentsRepo.PagePayments(
                    con, pool.Config.Id, page, pageSize))
                .Select(mapper.Map<Responses.Payment>)
                .ToArray();

            // enrich payments
            CoinMetaData.PaymentInfoLinks.TryGetValue(pool.Config.Coin.Type, out var txInfobaseUrl);
            CoinMetaData.AddressInfoLinks.TryGetValue(pool.Config.Coin.Type, out var addressInfobaseUrl);

            foreach (var payment in payments)
            {
                // compute transaction infoLink
                if (!string.IsNullOrEmpty(txInfobaseUrl))
                    payment.TransactionInfoLink = string.Format(txInfobaseUrl, payment.TransactionConfirmationData);

                // pool wallet link
                if (!string.IsNullOrEmpty(addressInfobaseUrl))
                    payment.AddressInfoLink = string.Format(addressInfobaseUrl, payment.Address);
            }

            await SendJson(context, payments);
        }

        private async Task HandleGetMinerStatsAsync(HttpContext context, Match m)
        {
            var pool = GetPool(context, m);
            if (pool == null)
                return;

            var address = m.Groups["address"]?.Value;
            if (string.IsNullOrEmpty(address))
            {
                context.Response.StatusCode = 404;
                return;
            }

            var statsResult = cf.Run(con => statsRepo.GetMinerStats(con, pool.Config.Id, address));
            Responses.MinerStats stats = null;

            if (statsResult != null)
            {
                stats = mapper.Map<Responses.MinerStats>(statsResult);

                // optional fields
                if (statsResult.LastPayment != null)
                {
                    // Set timestamp of last payment
                    stats.LastPayment = statsResult.LastPayment.Created;

                    // Compute info link
                    if (CoinMetaData.PaymentInfoLinks.TryGetValue(pool.Config.Coin.Type, out var baseUrl))
                        stats.LastPaymentLink = string.Format(baseUrl, statsResult.LastPayment.TransactionConfirmationData);
                }
            }

            await SendJson(context, stats);
        }

        private async Task HandleForceGcAsync(HttpContext context, Match m)
        {
            GC.Collect(2, GCCollectionMode.Forced);

            await SendJson(context, true);
        }

        private async Task HandleGcStatsAsync(HttpContext context, Match m)
        {
            // update other stats
            Program.gcStats.GcGen0 = GC.CollectionCount(0);
            Program.gcStats.GcGen1 = GC.CollectionCount(1);
            Program.gcStats.GcGen2 = GC.CollectionCount(2);
            Program.gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

            await SendJson(context, Program.gcStats);
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
            lock(pools)
            {
                pools.Add(pool);
            }
        }

        #endregion // API-Surface
    }
}
