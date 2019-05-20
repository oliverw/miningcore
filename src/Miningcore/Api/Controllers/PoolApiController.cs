using Autofac;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api.Extensions;
using Miningcore.Api.Responses;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Miningcore.Api.Controllers
{
    [Route("api/pools")]
    [ApiController]
    public class PoolApiController : ControllerBase
    {
        public PoolApiController(IComponentContext ctx)
        {
            clusterConfig = ctx.Resolve<ClusterConfig>();
            cf = ctx.Resolve<IConnectionFactory>();
            statsRepo = ctx.Resolve<IStatsRepository>();
            blocksRepo = ctx.Resolve<IBlockRepository>();
            paymentsRepo = ctx.Resolve<IPaymentRepository>();
            mapper = ctx.Resolve<IMapper>();
            clock = ctx.Resolve<IMasterClock>();
            pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        }

        private readonly ClusterConfig clusterConfig;
        private readonly IConnectionFactory cf;
        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IMapper mapper;
        private readonly IMasterClock clock;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;

        #region Actions

        [HttpGet]
        public async Task<GetPoolsResponse> Get()
        {
            var response = new GetPoolsResponse
            {
                Pools = await Task.WhenAll(clusterConfig.Pools.Where(x => x.Enabled).Select(async config =>
                {
                    // load stats
                    var stats = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, config.Id));

                    // get pool
                    pools.TryGetValue(config.Id, out var pool);

                    // map
                    var result = config.ToPoolInfo(mapper, stats, pool);

                    // enrich
                    result.TotalPaid = await cf.Run(con => statsRepo.GetTotalPoolPaymentsAsync(con, config.Id));
                    var from = clock.Now.AddDays(-1);

                    result.TopMiners = (await cf.Run(con => statsRepo.PagePoolMinersByHashrateAsync(
                            con, config.Id, from, 0, 15)))
                        .Select(mapper.Map<MinerPerformanceStats>)
                        .ToArray();

                    return result;
                }).ToArray())
            };

            return response;
        }

        [HttpGet("{poolId}")]
        public async Task<GetPoolResponse> GetPoolInfoAsync(string poolId)
        {
            var pool = GetPool(poolId);

            // load stats
            var stats = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, pool.Id));

            // get pool
            pools.TryGetValue(pool.Id, out var poolInstance);

            var response = new GetPoolResponse
            {
                Pool = pool.ToPoolInfo(mapper, stats, poolInstance)
            };

            // enrich
            response.Pool.TotalPaid = await cf.Run(con => statsRepo.GetTotalPoolPaymentsAsync(con, pool.Id));

            var from = clock.Now.AddDays(-1);

            response.Pool.TopMiners = (await cf.Run(con => statsRepo.PagePoolMinersByHashrateAsync(
                    con, pool.Id, from, 0, 15)))
                .Select(mapper.Map<MinerPerformanceStats>)
                .ToArray();

            return response;
        }

        [HttpGet("{poolId}/performance")]
        public async Task<GetPoolStatsResponse> GetPoolPerformanceAsync(string poolId,
            [FromQuery(Name = "r")] SampleRange range = SampleRange.Day,
            [FromQuery(Name = "i")] SampleInterval interval = SampleInterval.Hour)
        {
            var pool = GetPool(poolId);

            // set range
            var end = clock.Now;
            DateTime start;

            switch(range)
            {
                case SampleRange.Day:
                    start = end.AddDays(-1);
                    break;

                case SampleRange.Month:
                    start = end.AddDays(-30);
                    break;

                default:
                    throw new ApiException("invalid interval");
            }

            var stats = await cf.Run(con => statsRepo.GetPoolPerformanceBetweenAsync(
                con, pool.Id, interval, start, end));

            var response = new GetPoolStatsResponse
            {
                Stats = stats.Select(mapper.Map<AggregatedPoolStats>).ToArray()
            };

            return response;
        }

        [HttpGet("{poolId}/miners")]
        public async Task<MinerPerformanceStats[]> PagePoolMinersAsync(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            // set range
            var end = clock.Now;
            var start = end.AddDays(-1);

            var miners = (await cf.Run(con => statsRepo.PagePoolMinersByHashrateAsync(
                    con, pool.Id, start, page, pageSize)))
                .Select(mapper.Map<MinerPerformanceStats>)
                .ToArray();

            return miners;
        }

        [HttpGet("{poolId}/blocks")]
        public async Task<Responses.Block[]> PagePoolBlocksPagedAsync(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15, [FromQuery] BlockStatus[] state = null)
        {
            var pool = GetPool(poolId);

            var blockStates = state != null && state.Length > 0 ?
                state :
                new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

            var blocks = (await cf.Run(con => blocksRepo.PageBlocksAsync(con, pool.Id, blockStates, page, pageSize)))
                .Select(mapper.Map<Responses.Block>)
                .ToArray();

            // enrich blocks
            var blockInfobaseDict = pool.Template.ExplorerBlockLinks;

            foreach(var block in blocks)
            {
                // compute infoLink
                if(blockInfobaseDict != null)
                {
                    blockInfobaseDict.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl);

                    if(!string.IsNullOrEmpty(blockInfobaseUrl))
                    {
                        if(blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                        else if(blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                    }
                }
            }

            return blocks;
        }

        [HttpGet("{poolId}/payments")]
        public async Task<Responses.Payment[]> PagePoolPaymentsAsync(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            var payments = (await cf.Run(con => paymentsRepo.PagePaymentsAsync(
                    con, pool.Id, null, page, pageSize)))
                .Select(mapper.Map<Responses.Payment>)
                .ToArray();

            // enrich payments
            var txInfobaseUrl = pool.Template.ExplorerTxLink;
            var addressInfobaseUrl = pool.Template.ExplorerAccountLink;

            foreach(var payment in payments)
            {
                // compute transaction infoLink
                if(!string.IsNullOrEmpty(txInfobaseUrl))
                    payment.TransactionInfoLink = string.Format(txInfobaseUrl, payment.TransactionConfirmationData);

                // pool wallet link
                if(!string.IsNullOrEmpty(addressInfobaseUrl))
                    payment.AddressInfoLink = string.Format(addressInfobaseUrl, payment.Address);
            }

            return payments;
        }

        [HttpGet("{poolId}/miners/{address}")]
        public async Task<Responses.MinerStats> GetMinerInfoAsync(
            string poolId, string address, [FromQuery] SampleRange perfMode = SampleRange.Day)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException($"Invalid or missing miner address", HttpStatusCode.NotFound);

            var statsResult = await cf.RunTx((con, tx) =>
                statsRepo.GetMinerStatsAsync(con, tx, pool.Id, address), true, IsolationLevel.Serializable);

            Responses.MinerStats stats = null;

            if(statsResult != null)
            {
                stats = mapper.Map<Responses.MinerStats>(statsResult);

                // optional fields
                if(statsResult.LastPayment != null)
                {
                    // Set timestamp of last payment
                    stats.LastPayment = statsResult.LastPayment.Created;

                    // Compute info link
                    var baseUrl = pool.Template.ExplorerTxLink;
                    if(!string.IsNullOrEmpty(baseUrl))
                        stats.LastPaymentLink = string.Format(baseUrl, statsResult.LastPayment.TransactionConfirmationData);
                }

                stats.PerformanceSamples = await GetMinerPerformanceInternal(perfMode, pool, address);
            }

            return stats;
        }

        [HttpGet("{poolId}/miners/{address}/payments")]
        public async Task<Responses.Payment[]> PageMinerPaymentsAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException($"Invalid or missing miner address", HttpStatusCode.NotFound);

            var payments = (await cf.Run(con => paymentsRepo.PagePaymentsAsync(
                    con, pool.Id, address, page, pageSize)))
                .Select(mapper.Map<Responses.Payment>)
                .ToArray();

            // enrich payments
            var txInfobaseUrl = pool.Template.ExplorerTxLink;
            var addressInfobaseUrl = pool.Template.ExplorerAccountLink;

            foreach(var payment in payments)
            {
                // compute transaction infoLink
                if(!string.IsNullOrEmpty(txInfobaseUrl))
                    payment.TransactionInfoLink = string.Format(txInfobaseUrl, payment.TransactionConfirmationData);

                // pool wallet link
                if(!string.IsNullOrEmpty(addressInfobaseUrl))
                    payment.AddressInfoLink = string.Format(addressInfobaseUrl, payment.Address);
            }

            return payments;
        }

        [HttpGet("{poolId}/miners/{address}/balancechanges")]
        public async Task<Responses.BalanceChange[]> PageMinerBalanceChangesAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException($"Invalid or missing miner address", HttpStatusCode.NotFound);

            var balanceChanges = (await cf.Run(con => paymentsRepo.PageBalanceChangesAsync(
                    con, pool.Id, address, page, pageSize)))
                .Select(mapper.Map<Responses.BalanceChange>)
                .ToArray();

            return balanceChanges;
        }

        [HttpGet("{poolId}/miners/{address}/earnings/daily")]
        public async Task<AmountByDate[]> PageMinerEarningsByDayAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException($"Invalid or missing miner address", HttpStatusCode.NotFound);

            var earnings = (await cf.Run(con => paymentsRepo.PageMinerPaymentsByDayAsync(
                    con, pool.Id, address, page, pageSize)))
                .ToArray();

            return earnings;
        }

        [HttpGet("{poolId}/miners/{address}/performance")]
        public async Task<Responses.WorkerPerformanceStatsContainer[]> GetMinerPerformanceAsync(
            string poolId, string address, [FromQuery] SampleRange mode = SampleRange.Day)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException($"Invalid or missing miner address", HttpStatusCode.NotFound);

            var result = await GetMinerPerformanceInternal(mode, pool, address);

            return result;
        }

        #endregion // Actions

        private PoolConfig GetPool(string poolId)
        {
            if(string.IsNullOrEmpty(poolId))
                throw new ApiException($"Invalid pool id", HttpStatusCode.NotFound);

            var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

            if(pool == null)
                throw new ApiException($"Pool {poolId} is not known", HttpStatusCode.NotFound);

            return pool;
        }

        private async Task<Responses.WorkerPerformanceStatsContainer[]> GetMinerPerformanceInternal(
            SampleRange mode, PoolConfig pool, string address)
        {
            Persistence.Model.Projections.WorkerPerformanceStatsContainer[] stats = null;
            var end = clock.Now;
            DateTime start;

            switch(mode)
            {
                case SampleRange.Day:
                    // set range
                    if(end.Minute < 30)
                        end = end.AddHours(-1);

                    end = end.AddMinutes(-end.Minute);
                    end = end.AddSeconds(-end.Second);

                    start = end.AddDays(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenHourlyAsync(
                        con, pool.Id, address, start, end));
                    break;

                case SampleRange.Month:
                    if(end.Hour < 12)
                        end = end.AddDays(-1);

                    end = end.Date;

                    // set range
                    start = end.AddMonths(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenDailyAsync(
                        con, pool.Id, address, start, end));
                    break;
            }

            // map
            var result = mapper.Map<Responses.WorkerPerformanceStatsContainer[]>(stats);
            return result;
        }
    }
}
