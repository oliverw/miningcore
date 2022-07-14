using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.IO;
using Miningcore.Blockchain.Cryptonote.StratumRequests;
using Miningcore.Blockchain.Cryptonote.StratumResponses;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Nicehash;
using Miningcore.Notifications.Messages;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Blockchain.Cryptonote;

[CoinFamily(CoinFamily.Cryptonote)]
public class CryptonotePool : PoolBase
{
    public CryptonotePool(IComponentContext ctx,
        JsonSerializerSettings serializerSettings,
        IConnectionFactory cf,
        IStatsRepository statsRepo,
        IMapper mapper,
        IMasterClock clock,
        IMessageBus messageBus,
        RecyclableMemoryStreamManager rmsm,
        NicehashService nicehashService) :
        base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, rmsm, nicehashService)
    {
    }

    private long currentJobId;

    private CryptonoteJobManager manager;
    private string minerAlgo;

    private async Task OnLoginAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<CryptonoteWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var loginRequest = request.ParamsAs<CryptonoteLoginRequest>();

        if(string.IsNullOrEmpty(loginRequest?.Login))
            throw new StratumException(StratumError.MinusOne, "missing login");

        // extract worker/miner/paymentid
        var split = loginRequest.Login.Split('.');
        context.Miner = split[0].Trim();
        context.Worker = split.Length > 1 ? split[1].Trim() : null;
        context.UserAgent = loginRequest.UserAgent?.Trim();

        var addressToValidate = context.Miner;

        // extract paymentid
        var index = context.Miner.IndexOf('#');
        if(index != -1)
        {
            var paymentId = context.Miner[(index + 1)..].Trim();

            // validate
            if(!string.IsNullOrEmpty(paymentId) && paymentId.Length != CryptonoteConstants.PaymentIdHexLength)
                throw new StratumException(StratumError.MinusOne, "invalid payment id");

            // re-append to address
            addressToValidate = context.Miner[..index].Trim();
            context.Miner = addressToValidate + PayoutConstants.PayoutInfoSeperator + paymentId;
        }

        // validate login
        var result = manager.ValidateAddress(addressToValidate);

        context.IsSubscribed = result;
        context.IsAuthorized = result;

        if(context.IsAuthorized)
        {
            // extract control vars from password
            var passParts = loginRequest.Password?.Split(PasswordControlVarsSeparator);
            var staticDiff = GetStaticDiffFromPassparts(passParts);

            // Nicehash support
            var nicehashDiff = await GetNicehashStaticMinDiff(context, manager.Coin.Name, manager.Coin.GetAlgorithmName());

            if(nicehashDiff.HasValue)
            {
                if(!staticDiff.HasValue || nicehashDiff > staticDiff)
                {
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using API supplied difficulty of {nicehashDiff.Value}");

                    staticDiff = nicehashDiff;
                }

                else
                    logger.Info(() => $"[{connection.ConnectionId}] Nicehash detected. Using miner supplied difficulty of {staticDiff.Value}");
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                   context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{connection.ConnectionId}] Static difficulty set to {staticDiff.Value}");
            }

            // respond
            var loginResponse = new CryptonoteLoginResponse
            {
                Id = connection.ConnectionId,
                Job = CreateWorkerJob(connection)
            };

            await connection.RespondAsync(loginResponse, request.Id);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{connection.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{connection.ConnectionId}] Authorized miner {context.Miner}");
        }

        else
        {
            await connection.RespondErrorAsync(StratumError.MinusOne, "invalid login", request.Id);

            if(clusterConfig?.Banning?.BanOnLoginFailure is null or true)
            {
                logger.Info(() => $"[{connection.ConnectionId}] Banning unauthorized worker {context.Miner} for {loginFailureBanTimeout.TotalSeconds} sec");

                banManager.Ban(connection.RemoteEndpoint.Address, loginFailureBanTimeout);

                Disconnect(connection);
            }
        }
    }

    private async Task OnGetJobAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<CryptonoteWorkerContext>();

        if(request.Id == null)
            throw new StratumException(StratumError.MinusOne, "missing request id");

        var getJobRequest = request.ParamsAs<CryptonoteGetJobRequest>();

        // validate worker
        if(connection.ConnectionId != getJobRequest?.WorkerId || !context.IsAuthorized)
            throw new StratumException(StratumError.MinusOne, "unauthorized");

        // respond
        var job = CreateWorkerJob(connection);
        await connection.RespondAsync(job, request.Id);
    }

    private CryptonoteJobParams CreateWorkerJob(StratumConnection connection)
    {
        var context = connection.ContextAs<CryptonoteWorkerContext>();
        var job = new CryptonoteWorkerJob(NextJobId(), context.Difficulty);

        manager.PrepareWorkerJob(job, out var blob, out var target);

        // should never happen
        if(string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(blob))
            return null;

        var result = new CryptonoteJobParams
        {
            JobId = job.Id,
            Blob = blob,
            Target = target,
            Height = job.Height,
            SeedHash = job.SeedHash,
        };

        if(!string.IsNullOrEmpty(minerAlgo))
            result.Algorithm = minerAlgo;

        // update context
        lock(context)
        {
            context.AddJob(job);
        }

        return result;
    }

    private async Task OnSubmitAsync(StratumConnection connection, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<CryptonoteWorkerContext>();

        try
        {
            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            // check age of submission (aged submissions are usually caused by high server load)
            var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

            if(requestAge > maxShareAge)
            {
                logger.Warn(() => $"[{connection.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                return;
            }

            // check request
            var submitRequest = request.ParamsAs<CryptonoteSubmitShareRequest>();

            // validate worker
            if(connection.ConnectionId != submitRequest?.WorkerId || !context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // recognize activity
            context.LastActivity = clock.Now;

            CryptonoteWorkerJob job;

            lock(context)
            {
                var jobId = submitRequest?.JobId;

                if((job = context.FindJob(jobId)) == null)
                    throw new StratumException(StratumError.MinusOne, "invalid jobid");
            }

            // dupe check
            if(!job.Submissions.TryAdd(submitRequest.Nonce, true))
                throw new StratumException(StratumError.MinusOne, "duplicate share");

            // submit
            var share = await manager.SubmitShareAsync(connection, submitRequest, job, ct);
            await connection.RespondAsync(new CryptonoteResponseBase(), request.Id);

            // publish
            messageBus.SendMessage(new StratumShare(connection, share));

            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

            logger.Info(() => $"[{connection.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

            // update pool stats
            if(share.IsBlockCandidate)
                poolStats.LastPoolBlockTime = clock.Now;

            // update client stats
            context.Stats.ValidShares++;

            await UpdateVarDiffAsync(connection, false, ct);
        }

        catch(StratumException ex)
        {
            // telemetry
            PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

            // update client stats
            context.Stats.InvalidShares++;
            logger.Info(() => $"[{connection.ConnectionId}] Share rejected: {ex.Message} [{context.UserAgent}]");

            // banning
            ConsiderBan(connection, context, poolConfig.Banning);

            throw;
        }
    }

    private string NextJobId()
    {
        return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
    }

    private async Task OnNewJobAsync()
    {
        logger.Info(() => "Broadcasting jobs");

        await Guard(() => ForEachMinerAsync(async (connection, ct) =>
        {
            // send job
            var job = CreateWorkerJob(connection);
            await connection.NotifyAsync(CryptonoteStratumMethods.JobNotify, job);
        }));
    }

    #region Overrides

    protected override async Task SetupJobManager(CancellationToken ct)
    {
        manager = ctx.Resolve<CryptonoteJobManager>();
        manager.Configure(poolConfig, clusterConfig);

        await manager.StartAsync(ct);

        if(poolConfig.EnableInternalStratum == true)
        {
            minerAlgo = GetMinerAlgo();

            disposables.Add(manager.Blocks
                .Select(_ => Observable.FromAsync(() =>
                    Guard(OnNewJobAsync,
                        ex=> logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}"))))
                .Concat()
                .Subscribe(_ => { }, ex =>
                {
                    logger.Debug(ex, nameof(OnNewJobAsync));
                }));

            // start with initial blocktemplate
            await manager.Blocks.Take(1).ToTask(ct);
        }

        else
        {
            // keep updating NetworkStats
            disposables.Add(manager.Blocks.Subscribe());
        }
    }

    private string GetMinerAlgo()
    {
        switch(manager.Coin.Hash)
        {
            case CryptonightHashType.RandomX:
                return $"rx/{manager.Coin.HashVariant}";
        }

        return null;
    }

    protected override async Task InitStatsAsync(CancellationToken ct)
    {
        await base.InitStatsAsync(ct);

        blockchainStats = manager.BlockchainStats;
    }

    protected override WorkerContextBase CreateWorkerContext()
    {
        return new CryptonoteWorkerContext();
    }

    protected override async Task OnRequestAsync(StratumConnection connection,
        Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
    {
        var request = tsRequest.Value;
        var context = connection.ContextAs<CryptonoteWorkerContext>();

        try
        {
            switch(request.Method)
            {
                case CryptonoteStratumMethods.Login:
                    await OnLoginAsync(connection, tsRequest);
                    break;

                case CryptonoteStratumMethods.GetJob:
                    await OnGetJobAsync(connection, tsRequest);
                    break;

                case CryptonoteStratumMethods.Submit:
                    await OnSubmitAsync(connection, tsRequest, ct);
                    break;

                case CryptonoteStratumMethods.KeepAlive:
                    // recognize activity
                    context.LastActivity = clock.Now;
                    break;

                default:
                    logger.Debug(() => $"[{connection.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    await connection.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        catch(StratumException ex)
        {
            await connection.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
        }
    }

    public override double HashrateFromShares(double shares, double interval)
    {
        var result = shares / interval;
        return result;
    }

    public override double ShareMultiplier => 1;

    protected override async Task OnVarDiffUpdateAsync(StratumConnection connection, double newDiff, CancellationToken ct)
    {
        await base.OnVarDiffUpdateAsync(connection, newDiff, ct);

        if(connection.Context.ApplyPendingDifficulty())
        {
            // re-send job
            var job = CreateWorkerJob(connection);
            await connection.NotifyAsync(CryptonoteStratumMethods.JobNotify, job);
        }
    }

    #endregion // Overrides
}
