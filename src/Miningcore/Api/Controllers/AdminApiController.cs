using Autofac;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using System.Collections.Concurrent;
using System.Net;
using NLog;

namespace Miningcore.Api.Controllers;

[Route("api/admin")]
[ApiController]
public class AdminApiController : ApiControllerBase
{
    public AdminApiController(IComponentContext ctx) : base(ctx)
    {
        gcStats = ctx.Resolve<Responses.AdminGcStats>();
        minerRepo = ctx.Resolve<IMinerRepository>();
        pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        paymentsRepo = ctx.Resolve<IPaymentRepository>();
        balanceRepo = ctx.Resolve<IBalanceRepository>();
    }

    private readonly IPaymentRepository paymentsRepo;
    private readonly IBalanceRepository balanceRepo;
    private readonly IMinerRepository minerRepo;
    private readonly ConcurrentDictionary<string, IMiningPool> pools;

    private readonly Responses.AdminGcStats gcStats;

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    #region Actions

    [HttpGet("stats/gc")]
    public ActionResult<Responses.AdminGcStats> GetGcStats()
    {
        gcStats.GcGen0 = GC.CollectionCount(0);
        gcStats.GcGen1 = GC.CollectionCount(1);
        gcStats.GcGen2 = GC.CollectionCount(2);
        gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

        return gcStats;
    }

    [HttpPost("forcegc")]
    public ActionResult<string> ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced);
        return "Ok";
    }

    [HttpGet("pools/{poolId}/miners/{address}/getbalance")]
    public async Task<decimal> GetMinerBalanceAsync(string poolId, string address)
    {
        return await cf.Run(con => balanceRepo.GetBalanceAsync(con, poolId, address));
    }

    [HttpGet("pools/{poolId}/miners/{address}/settings")]
    public async Task<Responses.MinerSettings> GetMinerSettingsAsync(string poolId, string address)
    {
        var pool = GetPool(poolId);

        if(string.IsNullOrEmpty(address))
            throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

        var result = await cf.Run(con=> minerRepo.GetSettingsAsync(con, null, pool.Id, address));

        if(result == null)
            throw new ApiException("No settings found", HttpStatusCode.NotFound);

        return mapper.Map<Responses.MinerSettings>(result);
    }

    [HttpPost("pools/{poolId}/miners/{address}/settings")]
    public async Task<Responses.MinerSettings> SetMinerSettingsAsync(string poolId, string address,
        [FromBody] Responses.MinerSettings settings)
    {
        var pool = GetPool(poolId);

        if(string.IsNullOrEmpty(address))
            throw new ApiException("Invalid or missing miner address", HttpStatusCode.NotFound);

        if(settings == null)
            throw new ApiException("Invalid or missing settings", HttpStatusCode.BadRequest);

        // map settings
        var mapped = mapper.Map<Persistence.Model.MinerSettings>(settings);

        // clamp limit
        if(pool.PaymentProcessing != null)
            mapped.PaymentThreshold = Math.Max(mapped.PaymentThreshold, pool.PaymentProcessing.MinimumPayment);

        mapped.PoolId = pool.Id;
        mapped.Address = address;

        var result = await cf.RunTx(async (con, tx) =>
        {
            await minerRepo.UpdateSettingsAsync(con, tx, mapped);

            return await minerRepo.GetSettingsAsync(con, tx, mapped.PoolId, mapped.Address);
        });

        logger.Info(()=> $"Updated settings for pool {pool.Id}, miner {address}");

        return mapper.Map<Responses.MinerSettings>(result);
    }

    #endregion // Actions
}
