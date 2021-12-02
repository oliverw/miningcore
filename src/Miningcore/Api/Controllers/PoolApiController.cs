using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Miningcore.Api.Extensions;
using Miningcore.Api.Responses;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using NLog;

namespace Miningcore.Api.Controllers
{
    [Route("api/pools")]
    [ApiController]
    public class PoolApiController : ApiControllerBase
    {
        public PoolApiController(IComponentContext ctx, IActionDescriptorCollectionProvider _adcp) : base(ctx)
        {
            statsRepo = ctx.Resolve<IStatsRepository>();
            blocksRepo = ctx.Resolve<IBlockRepository>();
            minerRepo = ctx.Resolve<IMinerRepository>();
            shareRepo = ctx.Resolve<IShareRepository>();
            paymentsRepo = ctx.Resolve<IPaymentRepository>();
            clock = ctx.Resolve<IMasterClock>();
            pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
            adcp = _adcp;
        }

        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IMinerRepository minerRepo;
        private readonly IShareRepository shareRepo;
        private readonly IMasterClock clock;
        private readonly IActionDescriptorCollectionProvider adcp;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;

        private static readonly NLog.ILogger logger = LogManager.GetCurrentClassLogger();

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
                    result.TotalBlocks = await cf.Run(con => blocksRepo.GetPoolBlockCountAsync(con, config.Id));
                    var lastBlockTime = await cf.Run(con => blocksRepo.GetLastPoolBlockTimeAsync(con, config.Id));
                    result.LastPoolBlockTime = lastBlockTime;

                    if(lastBlockTime.HasValue) {
                        DateTime startTime = lastBlockTime.Value;
                        logger.Info(() => "[API] Creating Pool Effort and Round Shares For API Response");
                        var totalRoundShares = await cf.Run(con => shareRepo.CountAllSharesBetweenCreatedAsync(con, config.Id, startTime, clock.Now));
                        var totalRoundHashes = await cf.Run(con => shareRepo.GetTotalShareDiffBetweenCreatedAsync(con, config.Id, pool.ShareMultiplier, startTime, clock.Now));
                        var poolEffort = await cf.Run(con => shareRepo.GetEffortBetweenCreatedAsync(con, config.Id, pool.ShareMultiplier, startTime, clock.Now));
                        result.RoundShares = totalRoundShares;
                        result.RoundHashes = totalRoundHashes.Value;
                        result.PoolEffort = poolEffort.Value;
                    }
                    else
                    {
                        logger.Warn(() => "[API] API failed to generate pool effort and round shares stats! These stats shall be displayed as 0.");
                        result.RoundShares = 0;
                        result.RoundHashes = 0;
                        result.PoolEffort = 0;
                    }
                    result.PaymentProcessing.Extra = null;
                    // Copy ports to hide info without changing Config
                    Dictionary<int, PoolEndpoint> portsCopy = new Dictionary<int, PoolEndpoint>();
                    foreach(int key in result.Ports.Keys)
                    {
                        PoolEndpoint poolEP = result.Ports[key];
                        
                        var newPoolEP = new PoolEndpoint
                        {
                            Name = poolEP.Name,
                            VarDiff = poolEP.VarDiff,
                            TlsPfxPassword = poolEP.TlsPfxPassword,
                            TlsPfxFile = poolEP.TlsPfxFile,
                            ListenAddress = poolEP.ListenAddress,
                            Tls = poolEP.Tls,
                            TcpProxyProtocol = poolEP.TcpProxyProtocol,
                            Difficulty = poolEP.Difficulty,
                          
                        };

                        if(newPoolEP.Tls == true || newPoolEP.Tls == false)
                        {
                            newPoolEP.Tls = false;
                        }
                        if(!String.IsNullOrEmpty(poolEP.TlsPfxFile))
                        {
                            newPoolEP.TlsPfxFile = null;
                        }
                        if(!String.IsNullOrEmpty(poolEP.TlsPfxPassword))
                        {
                            newPoolEP.TlsPfxPassword = null;
                        }
                        portsCopy.Add(key, newPoolEP);
                    }
                    result.Ports = portsCopy;
                    //result.RoundShares = 
                    var from = clock.Now.AddDays(-1);

                    var minersByHashrate = await cf.Run(con => statsRepo.PagePoolMinersByHashrateAsync(con, config.Id, from, 0, 15));

                    result.TopMiners = minersByHashrate.Select(mapper.Map<MinerPerformanceStats>).ToArray();

                    return result;
                }).ToArray())
            };

            return response;
        }

        [HttpGet("/api/help")]
        public ActionResult GetHelp()
        {
            var tmp = adcp.ActionDescriptors.Items
                .Select(x =>
                {
                    // Get and pad http method
                    var method = x?.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.First();
                    method = $"{method,-5}";

                    return $"{method} -> {x.AttributeRouteInfo.Template}";
                });

            // convert curly braces
            var result = string.Join("\n", tmp).Replace("{", "<").Replace("}", ">") + "\n";

            return Content(result);
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
            response.Pool.TotalBlocks = await cf.Run(con => blocksRepo.GetPoolBlockCountAsync(con, pool.Id));
            var lastBlockTime = await cf.Run(con => blocksRepo.GetLastPoolBlockTimeAsync(con, pool.Id));
            response.Pool.LastPoolBlockTime = lastBlockTime;

            if(lastBlockTime.HasValue)
            {
                DateTime startTime = lastBlockTime.Value;
                logger.Info(() => "[API] Creating Pool Effort and Round Shares For API Response");
                var totalRoundShares = await cf.Run(con => shareRepo.CountAllSharesBetweenCreatedAsync(con, pool.Id, startTime, clock.Now));
                var totalRoundHashes = await cf.Run(con => shareRepo.GetTotalShareDiffBetweenCreatedAsync(con, pool.Id, poolInstance.ShareMultiplier, startTime, clock.Now));
                var poolEffort = await cf.Run(con => shareRepo.GetEffortBetweenCreatedAsync(con, pool.Id, poolInstance.ShareMultiplier, startTime, clock.Now));
                response.Pool.RoundShares = totalRoundShares;
                response.Pool.RoundHashes = totalRoundHashes.Value;
                response.Pool.PoolEffort = poolEffort.Value;

            }
            else
            {
                logger.Warn(() => "[API] API failed to generate pool effort and round shares stats! These stats shall be displayed as 0.");
                response.Pool.RoundShares = 0;
                response.Pool.RoundHashes = 0;
                response.Pool.PoolEffort = 0;
            }
            response.Pool.PaymentProcessing.Extra = null;
            // Copy ports to hide info without changing Config
            Dictionary<int, PoolEndpoint> portsCopy = new Dictionary<int, PoolEndpoint>();
            foreach(int key in response.Pool.Ports.Keys)
            {
                PoolEndpoint poolEP = response.Pool.Ports[key];

                var newPoolEP = new PoolEndpoint
                {
                    Name = poolEP.Name,
                    VarDiff = poolEP.VarDiff,
                    TlsPfxPassword = poolEP.TlsPfxPassword,
                    TlsPfxFile = poolEP.TlsPfxFile,
                    ListenAddress = poolEP.ListenAddress,
                    Tls = poolEP.Tls,
                    TcpProxyProtocol = poolEP.TcpProxyProtocol,
                    Difficulty = poolEP.Difficulty,

                };

                if(newPoolEP.Tls == true || newPoolEP.Tls == false)
                {
                    newPoolEP.Tls = false;
                }
                if(!String.IsNullOrEmpty(poolEP.TlsPfxFile))
                {
                    newPoolEP.TlsPfxFile = null;
                }
                if(!String.IsNullOrEmpty(poolEP.TlsPfxPassword))
                {
                    newPoolEP.TlsPfxPassword = null;
                }
                portsCopy.Add(key, newPoolEP);
            }
            response.Pool.Ports = portsCopy;
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
        public async Task<Responses.Block[]> PagePoolBlocksAsync(
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

        [HttpGet("/api/v2/pools/{poolId}/blocks")]
        public async Task<PagedResultResponse<Responses.Block[]>> PagePoolBlocksV2Async(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15, [FromQuery] BlockStatus[] state = null)
        {
            var pool = GetPool(poolId);

            var blockStates = state != null && state.Length > 0 ?
                state :
                new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

            uint pageCount = (uint) Math.Floor((await cf.Run(con => blocksRepo.GetPoolBlockCountAsync(con, poolId))) / (double) pageSize);

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

            var response = new PagedResultResponse<Responses.Block[]>(blocks, pageCount);
            return response;
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

        [HttpGet("/api/v2/pools/{poolId}/payments")]
        public async Task<PagedResultResponse<Responses.Payment[]>> PagePoolPaymentsV2Async(
            string poolId, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            uint pageCount = (uint) Math.Floor((await cf.Run(con => paymentsRepo.GetPaymentsCountAsync(con, poolId))) / (double) pageSize);

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

            var response = new PagedResultResponse<Responses.Payment[]>(payments, pageCount);
            return response;
        }

        [HttpGet("{poolId}/miners/{address}")]
        public async Task<Responses.MinerStats> GetMinerInfoAsync(
            string poolId, string address, [FromQuery] SampleRange perfMode = SampleRange.Day)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            var statsResult = await cf.RunTx((con, tx) =>
                statsRepo.GetMinerStatsAsync(con, tx, pool.Id, address), true, IsolationLevel.Serializable);

            Responses.MinerStats stats = null;

            if(statsResult != null)
            {   
                stats = mapper.Map<Responses.MinerStats>(statsResult);
                int shareConst = 1;
                if(pool.Template.Name.Equals("Ergo"))
                    shareConst = 256;
                stats.PendingShares = stats.PendingShares * shareConst;
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
                logger.Info(() => $"[API] Estimating balance for miner {address}");
                stats.EstimatedBalance = await GetPPLNSMinerEstimatedPayment(pool.Id, address);
                logger.Info(() => $"[API] Balance estimation complete for {address}");
            }

            return stats;
        }

        [HttpGet("{poolId}/miners/{address}/payments")]
        public async Task<Responses.Payment[]> PageMinerPaymentsAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

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

        [HttpGet("/api/v2/pools/{poolId}/miners/{address}/payments")]
        public async Task<PagedResultResponse<Responses.Payment[]>> PageMinerPaymentsV2Async(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            uint pageCount = (uint) Math.Floor((await cf.Run(con => paymentsRepo.GetPaymentsCountAsync(con, poolId, address))) / (double) pageSize);

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

            var response = new PagedResultResponse<Responses.Payment[]>(payments, pageCount);
            return response;
        }

        [HttpGet("{poolId}/miners/{address}/balancechanges")]
        public async Task<Responses.BalanceChange[]> PageMinerBalanceChangesAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            var balanceChanges = (await cf.Run(con => paymentsRepo.PageBalanceChangesAsync(
                    con, pool.Id, address, page, pageSize)))
                .Select(mapper.Map<Responses.BalanceChange>)
                .ToArray();

            return balanceChanges;
        }

        [HttpGet("/api/v2/pools/{poolId}/miners/{address}/balancechanges")]
        public async Task<PagedResultResponse<Responses.BalanceChange[]>> PageMinerBalanceChangesV2Async(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            uint pageCount = (uint) Math.Floor((await cf.Run(con => paymentsRepo.GetBalanceChangesCountAsync(con, poolId, address))) / (double) pageSize);

            var balanceChanges = (await cf.Run(con => paymentsRepo.PageBalanceChangesAsync(
                    con, pool.Id, address, page, pageSize)))
                .Select(mapper.Map<Responses.BalanceChange>)
                .ToArray();

            var response = new PagedResultResponse<Responses.BalanceChange[]>(balanceChanges, pageCount);
            return response;
        }

        [HttpGet("{poolId}/miners/{address}/earnings/daily")]
        public async Task<AmountByDate[]> PageMinerEarningsByDayAsync(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            var earnings = (await cf.Run(con => paymentsRepo.PageMinerPaymentsByDayAsync(
                    con, pool.Id, address, page, pageSize)))
                .ToArray();

            return earnings;
        }

        [HttpGet("/api/v2/pools/{poolId}/miners/{address}/earnings/daily")]
        public async Task<PagedResultResponse<AmountByDate[]>> PageMinerEarningsByDayV2Async(
            string poolId, string address, [FromQuery] int page, [FromQuery] int pageSize = 15)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            uint pageCount = (uint) Math.Floor((await cf.Run(con => paymentsRepo.GetMinerPaymentsByDayCountAsync(con, poolId, address))) / (double) pageSize);

            var earnings = (await cf.Run(con => paymentsRepo.PageMinerPaymentsByDayAsync(
                    con, pool.Id, address, page, pageSize)))
                .ToArray();

            var response = new PagedResultResponse<AmountByDate[]>(earnings, pageCount);
            return response;
        }

        [HttpGet("{poolId}/miners/{address}/performance")]
        public async Task<Responses.WorkerPerformanceStatsContainer[]> GetMinerPerformanceAsync(
            string poolId, string address, [FromQuery] SampleRange mode = SampleRange.Day)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(pool.Template.Family == CoinFamily.Ethereum)
                address = address.ToLower();

            var result = await GetMinerPerformanceInternal(mode, pool, address);

            return result;
        }

        [HttpGet("{poolId}/miners/{address}/settings")]
        public async Task<Responses.MinerSettings> GetMinerSettingsAsync(string poolId, string address)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            var result = await cf.Run(con=> minerRepo.GetSettings(con, null, pool.Id, address));

            if(result == null)
                throw new ApiException("No settings found", HttpStatusCode.NotFound);

            return mapper.Map<Responses.MinerSettings>(result);
        }

        [HttpPost("{poolId}/miners/{address}/settings")]
        public async Task<Responses.MinerSettings> SetMinerSettingsAsync(string poolId, string address,
            [FromBody] Requests.UpdateMinerSettingsRequest request)
        {
            var pool = GetPool(poolId);

            if(string.IsNullOrEmpty(address))
                throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

            if(request?.Settings == null)
                throw new ApiException("Invalid or missing settings", HttpStatusCode.BadRequest);

            if(!IPAddress.TryParse(request.IpAddress, out var requestIp))
                throw new ApiException("Invalid IP address", HttpStatusCode.BadRequest);

            // fetch recent IPs
            var ips = await cf.Run(con=> shareRepo.GetRecentyUsedIpAddresses(con, null, poolId, address));

            // any known ips?
            if(ips == null || ips.Length == 0)
                throw new ApiException("Address not recently used for mining", HttpStatusCode.NotFound);

            // match?
            if(!ips.Any(x=> IPAddress.TryParse(x, out var ipAddress) && ipAddress.IsEqual(requestIp)))
                throw new ApiException("None of the recently used IP addresses matches the request", HttpStatusCode.Forbidden);

            // map settings
            var mapped = mapper.Map<Persistence.Model.MinerSettings>(request.Settings);

            // clamp limit
            if(pool.PaymentProcessing != null)
                mapped.PaymentThreshold = Math.Max(mapped.PaymentThreshold, pool.PaymentProcessing.MinimumPayment);

            mapped.PoolId = pool.Id;
            mapped.Address = address;

            // finally update the settings
            return await cf.RunTx(async (con, tx) =>
            {
                await minerRepo.UpdateSettings(con, tx, mapped);

                logger.Info(()=> $"Updated settings for pool {pool.Id}, miner {address}");

                var result = await minerRepo.GetSettings(con, tx, mapped.PoolId, mapped.Address);
                return mapper.Map<Responses.MinerSettings>(result);
            });
        }

        #endregion // Actions

        private async Task<Responses.WorkerPerformanceStatsContainer[]> GetMinerPerformanceInternal(
            SampleRange mode, PoolConfig pool, string address)
        {
            Persistence.Model.Projections.WorkerPerformanceStatsContainer[] stats = null;
            var end = clock.Now;
            DateTime start;

            switch(mode)
            {
                case SampleRange.Hour:
                    end = end.AddSeconds(-end.Second);

                    start = end.AddHours(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenThreeMinutelyAsync(con, pool.Id, address, start, end));
                    break;

                case SampleRange.Day:
                    // set range
                    if(end.Minute < 30)
                        end = end.AddHours(-1);

                    end = end.AddMinutes(-end.Minute);
                    end = end.AddSeconds(-end.Second);

                    start = end.AddDays(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenHourlyAsync(con, pool.Id, address, start, end));
                    break;

                case SampleRange.Month:
                    if(end.Hour < 12)
                        end = end.AddDays(-1);

                    end = end.Date;

                    // set range
                    start = end.AddMonths(-1);

                    stats = await cf.Run(con => statsRepo.GetMinerPerformanceBetweenDailyAsync(con, pool.Id, address, start, end));
                    break;
            }

            // map
            var result = mapper.Map<Responses.WorkerPerformanceStatsContainer[]>(stats);
            return result;
        }
        private async Task<decimal> GetPPLNSMinerEstimatedPayment(string poolId, string address)
        {
            var pendingBlocks = await cf.Run(con => blocksRepo.GetPendingBlocksForPoolAsync(con, poolId));
            var totalEstPayment = 0.0m;
            var pool = GetPool(poolId);
            var shareDiffConstant = 1;
            var window = 2.0m;
            // Some ugly constants for our specific ergo pool
            if(pool.Template.Name.Equals("Ergo"))
            {
                shareDiffConstant = 256;
                window = 0.5m;
            }
            logger.Info(() => $"[API] Estimating balance for miner {address} for {pendingBlocks.Length} pending blocks");
            foreach(Persistence.Model.Block block in pendingBlocks)
            {

                var estBlockPayment = 0.0m;
                var estBlockScore = 0.0m;
                var done = false;
                if(block.Reward > 0)
                {
                    
                    var blockReward = EstimateRewardAfterPoolFees(pool, block.Reward);
                    var pplnsShares = await cf.Run(con => shareRepo.ReadMinerSharesBeforeCreatedAsync(con, poolId, address, block.Created, false, 50000));
                    var blockRewardRemaining = blockReward;

                    foreach(Persistence.Model.Share minerShare in pplnsShares)
                    {
                        var shareScore = (decimal) ((minerShare.Difficulty * shareDiffConstant) / minerShare.NetworkDifficulty);
                        if(estBlockScore + shareScore >= window)
                        {
                            shareScore = window - estBlockScore;
                            done = true;
                        }
                        var reward = shareScore * blockReward / window;
                        estBlockScore += shareScore;
                        blockRewardRemaining -= reward;

                        // This should not be called
                        if(blockRewardRemaining <= 0 && !done)
                            throw new OverflowException("blockRewardRemaining < 0");

                        if(reward > 0)
                        {
                            estBlockPayment += reward;
                        }

                        if(done)
                            break;
                    }
                    totalEstPayment += estBlockPayment;
                   // logger.Info(() => $"[API] Estimated balance for miner {address} for block {block.BlockHeight}: {estBlockPayment}");
                }
            }
            logger.Info(() => $"[API] Total Estimated balance for miner {address}: {totalEstPayment}");
            return totalEstPayment;
        }
        private decimal EstimateRewardAfterPoolFees(PoolConfig poolConfig, decimal blockReward)
        {
            var blockRewardRemaining = blockReward;
            foreach(var recipient in poolConfig.RewardRecipients.Where(x => x.Percentage > 0))
            {
                var amount = blockReward * (recipient.Percentage / 100.0m);

                blockRewardRemaining -= amount;
            }
            return blockRewardRemaining;
        }
    }
}
