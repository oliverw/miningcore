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
using System.Linq;
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
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Monero
{
    [CoinMetadata(CoinType.XMR, CoinType.AEON, CoinType.ETN)]
    public class MoneroPool : PoolBase
    {
        public MoneroPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, notificationService)
        {
        }

        private long currentJobId;

        private MoneroJobManager manager;

        private void OnLogin(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<MoneroWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
                return;
            }

            var loginRequest = request.ParamsAs<MoneroLoginRequest>();

            if (string.IsNullOrEmpty(loginRequest?.Login))
            {
                client.RespondError(StratumError.MinusOne, "missing login", request.Id);
                return;
            }

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
            {
                client.RespondError(StratumError.MinusOne, "invalid login", request.Id);
                return;
            }

            // validate payment Id
            if (!string.IsNullOrEmpty(context.PaymentId) && context.PaymentId.Length != MoneroConstants.PaymentIdHexLength)
            {
                client.RespondError(StratumError.MinusOne, "invalid payment id", request.Id);
                return;
            }

            // extract control vars from password
            var staticDiff = GetStaticDiffFromPassparts(passParts);
            if (staticDiff.HasValue &&
                (context.VarDiff != null && staticDiff.Value >= context.VarDiff.Config.MinDiff ||
                    context.VarDiff == null && staticDiff.Value > context.Difficulty))
            {
                context.VarDiff = null; // disable vardiff
                context.SetDifficulty(staticDiff.Value);
            }

            // respond
            var loginResponse = new MoneroLoginResponse
            {
                Id = client.ConnectionId,
                Job = CreateWorkerJob(client)
            };

            client.Respond(loginResponse, request.Id);

            // log association
            logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] = {loginRequest.Login} = {client.RemoteEndpoint.Address}");
        }

        private void OnGetJob(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<MoneroWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.MinusOne, "missing request id", request.Id);
                return;
            }

            var getJobRequest = request.ParamsAs<MoneroGetJobRequest>();

            // validate worker
            if (client.ConnectionId != getJobRequest?.WorkerId || !context.IsAuthorized)
            {
                client.RespondError(StratumError.MinusOne, "unauthorized", request.Id);
                return;
            }

            // respond
            var job = CreateWorkerJob(client);
            client.Respond(job, request.Id);
        }

        private MoneroJobParams CreateWorkerJob(StratumClient client)
        {
            var context = client.GetContextAs<MoneroWorkerContext>();
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

        private async Task OnSubmitAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<MoneroWorkerContext>();

            try
            {
                if (request.Id == null)
                    throw new StratumException(StratumError.MinusOne, "missing request id");

                // check age of submission (aged submissions are usually caused by high server load)
                var requestAge = clock.Now - tsRequest.Timestamp.UtcDateTime;

                if (requestAge > maxShareAge)
                {
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Dropping stale share submission request (not client's fault)");
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

                    if (string.IsNullOrEmpty(jobId) ||
                        (job = context.ValidJobs.FirstOrDefault(x => x.Id == jobId)) == null)
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

                var share = await manager.SubmitShareAsync(client, submitRequest, job, poolEndpoint.Difficulty);

                // success
                client.Respond(new MoneroResponseBase(), request.Id);
				shareSubject.OnNext(new ClientShare(client, share));

				logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share accepted: D={Math.Round(share.Difficulty, 3)}");

                // update pool stats
                if (share.IsBlockCandidate)
                    poolStats.LastPoolBlockTime = clock.Now;

                // update client stats
                context.Stats.ValidShares++;
                UpdateVarDiff(client);
            }

            catch (StratumException ex)
            {
                client.RespondError(ex.Code, ex.Message, request.Id, false);

                // update client stats
                context.Stats.InvalidShares++;
                logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Share rejected: {ex.Message}");

                // banning
                ConsiderBan(client, context, poolConfig.Banning);
            }
        }

        private string NextJobId()
        {
            return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
        }

        private void OnNewJob()
        {
            logger.Info(() => $"[{LogCat}] Broadcasting job");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<MoneroWorkerContext>();

                if (context.IsSubscribed && context.IsAuthorized)
                {
                    // check alive
                    var lastActivityAgo = clock.Now - context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // send job
                    var job = CreateWorkerJob(client);
                    client.Notify(MoneroStratumMethods.JobNotify, job);
                }
            });
        }

        #region Overrides

        protected override async Task SetupJobManager()
        {
            manager = ctx.Resolve<MoneroJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync();

            if (poolConfig.EnableInternalStratum == true)
	        {
		        disposables.Add(manager.Blocks.Subscribe(_ => OnNewJob()));

		        // we need work before opening the gates
		        await manager.Blocks.Take(1).ToTask();
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
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<MoneroWorkerContext>();

            switch (request.Method)
            {
                case MoneroStratumMethods.Login:
                    OnLogin(client, tsRequest);
                    break;

                case MoneroStratumMethods.GetJob:
                    OnGetJob(client, tsRequest);
                    break;

                case MoneroStratumMethods.Submit:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case MoneroStratumMethods.KeepAlive:
                    // recognize activity
                    context.LastActivity = clock.Now;
                    break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var result = shares / interval;
            return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            base.OnVarDiffUpdate(client, newDiff);

            // apply immediately and notify client
            var context = client.GetContextAs<MoneroWorkerContext>();

            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                // re-send job
                var job = CreateWorkerJob(client);
                client.Notify(MoneroStratumMethods.JobNotify, job);
            }
        }

        #endregion // Overrides
    }
}
