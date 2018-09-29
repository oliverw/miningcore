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
using MiningCore.Blockchain.Monero.StratumRequests;
using MiningCore.Blockchain.Monero.StratumResponses;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Messaging;
using MiningCore.Mining;
using MiningCore.Notifications.Messages;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero
{
    [CoinMetadata(CoinType.XMR, CoinType.AEON, CoinType.ETN, CoinType.TUBE)]
    public class MoneroPool : PoolBase
    {
        public MoneroPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            IMessageBus messageBus) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, messageBus)
        {
        }

        private long currentJobId;

        private MoneroJobManager manager;

        private async Task OnLoginAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            var context = client.ContextAs<MoneroWorkerContext>();

            if (request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var loginRequest = request.ParamsAs<MoneroLoginRequest>();

            if (string.IsNullOrEmpty(loginRequest?.Login))
                throw new StratumException(StratumError.MinusOne, "missing login");

            // extract worker/miner/paymentid
            var split = loginRequest.Login.Split('.');
            context.MinerName = split[0].Trim();
            context.WorkerName = split.Length > 1 ? split[1].Trim() : null;
            context.UserAgent = loginRequest.UserAgent?.Trim();
            var passParts = loginRequest.Password?.Split(PasswordControlVarsSeparator);

            // extract paymentid
            var index = context.MinerName.IndexOf('#');
            if (index != -1)
            {
                context.PaymentId = context.MinerName.Substring(index + 1).Trim();
                context.MinerName = context.MinerName.Substring(0, index).Trim();
            }

            // validate login
            var result = manager.ValidateAddress(context.MinerName);

            context.IsSubscribed = result;
            context.IsAuthorized = result;

            if (!context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "invalid login");

            // validate payment Id
            if (!string.IsNullOrEmpty(context.PaymentId) && context.PaymentId.Length != MoneroConstants.PaymentIdHexLength)
                throw new StratumException(StratumError.MinusOne, "invalid payment id");

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);
            if (staticDiff.HasValue &&
                (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                    context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);

                logger.Info(() => $"[{client.ConnectionId}] Setting static difficulty of {staticDiff.Value}");
            }

            // respond
            var loginResponse = new MoneroLoginResponse
            {
                Id = client.ConnectionId,
                Job = CreateWorkerJob(client)
            };

            await client.RespondAsync(loginResponse, request.Id);

            // log association
            logger.Info(() => $"[{client.ConnectionId}] Authorized worker {loginRequest.Login}");
        }

        private async Task OnGetJobAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            var context = client.ContextAs<MoneroWorkerContext>();

            if (request.Id == null)
                throw new StratumException(StratumError.MinusOne, "missing request id");

            var getJobRequest = request.ParamsAs<MoneroGetJobRequest>();

            // validate worker
            if (client.ConnectionId != getJobRequest?.WorkerId || !context.IsAuthorized)
                throw new StratumException(StratumError.MinusOne, "unauthorized");

            // respond
            var job = CreateWorkerJob(client);
            await client.RespondAsync(job, request.Id);
        }

        private MoneroJobParams CreateWorkerJob(StratumClient client)
        {
            var context = client.ContextAs<MoneroWorkerContext>();
            var job = new MoneroWorkerJob(NextJobId(), context.Difficulty);

            manager.PrepareWorkerJob(job, out var blob, out var target);

            // should never happen
            if (string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(blob))
                return null;

            var result = new MoneroJobParams
            {
                JobId = job.Id,
                Blob = blob,
                Target = target
            };

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
            var context = client.ContextAs<MoneroWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Warn(() => $"[{client.ConnectionId}] Dropping stale share submission request (server overloaded?)");
                    return;
                }

                // check request
                var submitRequest = request.ParamsAs<MoneroSubmitShareRequest>();

                // validate worker
                if (client.ConnectionId != submitRequest?.WorkerId || !context.IsAuthorized)
                    throw new StratumException(StratumError.MinusOne, "unauthorized");

                // recognize activity
                context.LastActivity = clock.Now;

                MoneroWorkerJob job;

                lock(context)
                {
                    var jobId = submitRequest?.JobId;

                    if ((job = context.FindJob(jobId)) == null)
                        throw new StratumException(StratumError.MinusOne, "invalid jobid");
                }

                // dupe check
                var nonceLower = submitRequest.Nonce.ToLower();

                lock(job)
                {
                    if (job.Submissions.Contains(nonceLower))
                        throw new StratumException(StratumError.MinusOne, "duplicate share");

                    job.Submissions.Add(nonceLower);
                }

                var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                var share = await manager.SubmitShareAsync(client, submitRequest, job, poolEndpoint.Difficulty, ct);
                await client.RespondAsync(new MoneroResponseBase(), request.Id);

                // publish
                messageBus.SendMessage(new ClientShare(client, share));

                // telemetry
                PublishTelemetry(TelemetryCategory.Share, clock.Now - tsRequest.Timestamp.UtcDateTime, true);

                logger.Info(() => $"[{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

                // update pool stats
                if (share.IsBlockCandidate)
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
            logger.Info(() => $"[Broadcasting job");

            var tasks = ForEachClient(async client =>
            {
                if (!client.IsAlive)
                    return;

                var context = client.ContextAs<MoneroWorkerContext>();

                if (context.IsSubscribed && context.IsAuthorized)
                {
                    // check alive
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[[{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // send job
                    var job = CreateWorkerJob(client);
                    await client.NotifyAsync(MoneroStratumMethods.JobNotify, job);
                }
            });

            return Task.WhenAll(tasks);
        }

        #region Overrides

        protected override async Task SetupJobManager(CancellationToken ct)
        {
            manager = ctx.Resolve<MoneroJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync(ct);

            if (poolConfig.EnableInternalStratum == true)
            {
                disposables.Add(manager.Blocks
                    .Select(x => Observable.FromAsync(() => OnNewJobAsync()))
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

        protected override void InitStats()
        {
            base.InitStats();

            blockchainStats = manager.BlockchainStats;
        }

        protected override WorkerContextBase CreateClientContext()
        {
            return new MoneroWorkerContext();
        }

        protected override async Task OnRequestAsync(StratumClient client,
            Timestamped<JsonRpcRequest> tsRequest, CancellationToken ct)
        {
            var request = tsRequest.Value;
            var context = client.ContextAs<MoneroWorkerContext>();

            try
            {
                switch(request.Method)
                {
                    case MoneroStratumMethods.Login:
                        await OnLoginAsync(client, tsRequest);
                        break;

                    case MoneroStratumMethods.GetJob:
                        await OnGetJobAsync(client, tsRequest);
                        break;

                    case MoneroStratumMethods.Submit:
                        await OnSubmitAsync(client, tsRequest, ct);
                        break;

                    case MoneroStratumMethods.KeepAlive:
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
            var context = client.ContextAs<MoneroWorkerContext>();

            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                // re-send job
                var job = CreateWorkerJob(client);
                await client.NotifyAsync(MoneroStratumMethods.JobNotify, job);
            }
        }

        #endregion // Overrides
    }
}
