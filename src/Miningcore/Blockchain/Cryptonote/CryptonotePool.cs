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
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
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

namespace Miningcore.Blockchain.Cryptonote
{
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
            NicehashService nicehashService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus, nicehashService)
        {
        }

        private long currentJobId;

        private CryptonoteJobManager manager;
        private string minerAlgo;

        private async Task OnLoginAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<CryptonoteWorkerContext>();

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
            if(!result)
                throw new StratumException(StratumError.MinusOne, "invalid login");

            context.IsSubscribed = result;
            context.IsAuthorized = result;

            // Nicehash support
            double? staticDiff = null;

            if(clusterConfig.Nicehash?.Enable == true &&
               context.UserAgent.Contains("nicehash", StringComparison.OrdinalIgnoreCase))
            {
                // query current diff
                staticDiff = await nicehashService.GetStaticDiff(manager.Coin.Name, manager.Coin.GetAlgorithmName(), CancellationToken.None);

                if(staticDiff.HasValue)
                    logger.Info(()=> $"[{client.ConnectionId}] Nicehash detected. Using static difficulty of {staticDiff.Value}");
            }

            if(!staticDiff.HasValue)
            {
                // extract control vars from password
                var passParts = loginRequest.Password?.Split(PasswordControlVarsSeparator);
                staticDiff = GetStaticDiffFromPassparts(passParts);
            }

            // Static diff
            if(staticDiff.HasValue &&
               (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{client.ConnectionId}] Static difficulty set to {staticDiff.Value}");
            }

            // respond
            var loginResponse = new CryptonoteLoginResponse
            {
                Id = client.ConnectionId,
                Job = CreateWorkerJob(client)
            };

            await client.RespondAsync(loginResponse, request.Id);

            // log association
            if(!string.IsNullOrEmpty(context.Worker))
                logger.Info(() => $"[{client.ConnectionId}] Authorized worker {context.Worker}@{context.Miner}");
            else
                logger.Info(() => $"[{client.ConnectionId}] Authorized miner {context.Miner}");
        }

        private async Task OnGetJobAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<CryptonoteWorkerContext>();

            if(request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var getJobRequest = request.ParamsAs<CryptonoteGetJobRequest>();

            // validate worker
            if(client.ConnectionId != getJobRequest?.WorkerId || !context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // respond
            var job = CreateWorkerJob(client);
            await client.RespondAsync(job, request.Id);
        }

        private CryptonoteJobParams CreateWorkerJob(StratumClient client)
        {
            var context = client.ContextAs<CryptonoteWorkerContext>();
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

        private async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<CryptonoteWorkerContext>();

            try
            {
                if(request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if(requestAge > maxShareAge)
                {
                    logger.Warn(() => $"[{client.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                    return;
                }

                // check request
                var submitRequest = request.ParamsAs<CryptonoteSubmitShareRequest>();

                // validate worker
                if(client.ConnectionId != submitRequest?.WorkerId || !context.IsAuthorized)
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

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, job, poolEndpoint.Difficulty, ct);
                await client.RespondAsync(new CryptonoteResponseBase(), request.Id);

                // publish
                messageBus.SendMessage(new ClientShare(client, share));

                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

                // update pool stats
                if(share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;
                await UpdateVarDiffAsync(client);
            }

            catch(StratumException ex)
            {
                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, false);

                // update client stats
                context.Stats.InvalidShares++;
                logger.Info(() => $"[{client.ConnectionId}] Share rejected: {ex.Message}");

                // banning
                ConsiderBan(client, context, poolConfig.Banning);

                throw;
            }
        }

        private string NextJobId()
        {
            return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
        }

        private Task OnNewJobAsync()
        {
            logger.Info(() => "Broadcasting job");

            var tasks = ForEachClient(async client =>
            {
                if(!client.IsAlive)
                    return;

                var context = client.ContextAs<CryptonoteWorkerContext>();

                if(context.IsSubscribed && context.IsAuthorized)
                {
                    // check alive
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if(poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[[{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // send job
                    var job = CreateWorkerJob(client);
                    await client.NotifyAsync(CryptonoteStratumMethods.JobNotify, job);
                }
            });

            return Task.WhenAll(tasks);
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
                    .Select(_ => Observable.FromAsync(async () =>
                    {
                        try
                        {
                            await OnNewJobAsync();
                        }

                        catch(Exception ex)
                        {
                            logger.Debug(() => $"{nameof(OnNewJobAsync)}: {ex.Message}");
                        }
                    }))
                    .Concat()
                    .Subscribe(_ => { }, ex =>
                    {
                        logger.Debug(ex, nameof(OnNewJobAsync));
                    }));

                // we need work before opening the gates
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

        protected override async Task InitStatsAsync()
        {
            await base.InitStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new CryptonoteWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<CryptonoteWorkerContext>();

            try
            {
                switch(request.Method)
                {
                    case CryptonoteStratumMethods.Login:
                        await OnLoginAsync(client, tsRequest);
                        break;

                    case CryptonoteStratumMethods.GetJob:
                        await OnGetJobAsync(client, tsRequest);
                        break;

                    case CryptonoteStratumMethods.Submit:
                        await OnSubmitAsync(client, tsRequest, ct);
                        break;

                    case CryptonoteStratumMethods.KeepAlive:
                        // recognize activity
                        context.LastActivity = clock.Now;
                        break;

                    default:
                        logger.Debug(() => $"[{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                        await client.RespondErrorAsync(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                        break;
                }
            }

            catch(StratumException ex)
            {
                await client.RespondErrorAsync(ex.Code, ex.Message, request.Id, false);
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var result = shares / interval;
            return result;
        }

        protected override async Task OnVarDiffUpdateAsync(StratumClient client, double newDiff)
        {
            await base.OnVarDiffUpdateAsync(client, newDiff);

            // apply immediately and notify client
            var context = client.ContextAs<CryptonoteWorkerContext>();

            if(context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                // re-send job
                var job = CreateWorkerJob(client);
                await client.NotifyAsync(CryptonoteStratumMethods.JobNotify, job);
            }
        }

        #endregion // Overrides
    }
}
