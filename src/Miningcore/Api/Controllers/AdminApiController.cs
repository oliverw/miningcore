using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api.Requests;
using Miningcore.Api.Responses;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using System.Collections.Concurrent;
using System.Net;
using NLog;

namespace Miningcore.Api.Controllers;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
        payoutManager = ctx.Resolve<PayoutManager>();

    }

    private readonly IPaymentRepository paymentsRepo;
    private readonly IBalanceRepository balanceRepo;
    private readonly IMinerRepository minerRepo;
    private readonly ConcurrentDictionary<string, IMiningPool> pools;
    private readonly PayoutManager payoutManager;

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

        var result = await cf.Run(con=> minerRepo.GetSettings(con, null, pool.Id, address));

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
            await minerRepo.UpdateSettings(con, tx, mapped);

            return await minerRepo.GetSettings(con, tx, mapped.PoolId, mapped.Address);
        });

        logger.Info(()=> $"Updated settings for pool {pool.Id}, miner {address}");

        return mapper.Map<Responses.MinerSettings>(result);
    }

    [HttpPost("pools/{poolId}/miners/{address}/forcePayout")]
    public async Task<string> ForcePayout(string poolId, string address)
    {
        logger.Info($"Forcing payout for {address}");
        try
        {
            if(string.IsNullOrEmpty(poolId))
            {
                throw new ApiException($"Invalid pool id", HttpStatusCode.NotFound);
            }

            // Get the IMiningPool by Id
            IMiningPool pool;
            pools.TryGetValue(poolId, out pool);

            if (pool == null)
            {
                throw new ApiException($"Pool {poolId} is not known", HttpStatusCode.NotFound);
            }

            return await payoutManager.PayoutSingleBalanceAsync(pool, address);
        }
        catch(Exception ex)
        {
            //rethrow as ApiException to be handled by ApiExceptionHandlingMiddleware
            throw new ApiException(ex.Message, HttpStatusCode.InternalServerError);
        }
    }

    [HttpPost("subtractBalance")]
    public async Task<SubtractBalanceResponse> SubtractBalance(SubtractBalanceRequest subtractBalanceRequest)
    {
        logger.Info($"Subtracting balance for {subtractBalanceRequest.Address}. PoolId: {subtractBalanceRequest.PoolId} Amount: {subtractBalanceRequest.Address}");

        if (subtractBalanceRequest.Amount <= 0)
        {
            logger.Error($"Invalid subtractBalance request. Amount is less than or equal to 0 - {subtractBalanceRequest.Amount}");
            throw new ApiException($"Invalid subtractBalance request. Amount is less than or equal to 0 - {subtractBalanceRequest.Amount}", HttpStatusCode.BadRequest);
        }

        var oldBalance = await cf.Run(con => balanceRepo.GetBalanceDataAsync(con, subtractBalanceRequest.PoolId, subtractBalanceRequest.Address));

        if (oldBalance.Amount < subtractBalanceRequest.Amount)
        {
            logger.Error($"Invalid subtractBalance request. Current balance is less than amount. Current balance: {oldBalance.Amount}. Amount: {subtractBalanceRequest.Amount}");
            throw new ApiException($"Invalid subtractBalance request. Current balance is less than amount. Current balance: {oldBalance.Amount}. Amount: {subtractBalanceRequest.Amount}", HttpStatusCode.BadRequest);
        }

        await cf.Run(con => balanceRepo.AddAmountAsync(con, null, subtractBalanceRequest.PoolId, subtractBalanceRequest.Address, -subtractBalanceRequest.Amount, "Subtract balance after forced payout"));

        var newBalance = await cf.Run(con => balanceRepo.GetBalanceDataAsync(con, subtractBalanceRequest.PoolId, subtractBalanceRequest.Address));

        logger.Info($"Successfully subtracted balance for {subtractBalanceRequest.Address}. Old Balance: {oldBalance.Amount}. New Balance: {newBalance.Amount}");

        return new SubtractBalanceResponse { OldBalance = oldBalance, NewBalance = newBalance };
    }

    #endregion // Actions
}
