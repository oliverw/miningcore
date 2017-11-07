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
using System.Reactive;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using Newtonsoft.Json;
using BigInteger = NBitcoin.BouncyCastle.Math.BigInteger;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashPoolBase<TJob> : BitcoinPoolBase<TJob, ZCashBlockTemplate>
        where TJob : ZCashJob, new()
    {
        public ZCashPoolBase(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, notificationService)
        {
        }

        protected override BitcoinJobManager<TJob, ZCashBlockTemplate> CreateJobManager()
        {
            return ctx.Resolve<ZCashJobManager<TJob>>(
                new TypedParameter(typeof(IExtraNonceProvider), new ZCashExtraNonceProvider()));
        }

        protected override void OnSubscribe(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();

            var data = new object[]
                {
                    client.ConnectionId,
                }
                .Concat(manager.GetSubscriberData(client))
                .ToArray();

            client.Respond(data, request.Id);

            // setup worker context
            client.Context.IsSubscribed = true;
            client.Context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected override async Task OnAuthorizeAsync(StratumClient<BitcoinWorkerContext> client, Timestamped<JsonRpcRequest> tsRequest)
        {
            await base.OnAuthorizeAsync(client, tsRequest);

            if (client.Context.IsAuthorized)
            {
                // send intial update
                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(client.Context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        protected override async Task OnRequestAsync(StratumClient<BitcoinWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch(request.Method)
            {
                case BitcoinStratumMethods.Subscribe:
                    OnSubscribe(client, tsRequest);
                    break;

                case BitcoinStratumMethods.Authorize:
                    await OnAuthorizeAsync(client, tsRequest);
                    break;

                case BitcoinStratumMethods.SubmitShare:
                    await OnSubmitAsync(client, tsRequest);
                    break;

                case ZCashStratumMethods.SuggestTarget:
                    //OnSuggestTarget(client, tsRequest);
                    break;

                case BitcoinStratumMethods.GetTransactions:
                    OnGetTransactions(client, tsRequest);
                    break;

                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        protected override void OnNewJob(object jobParams)
        {
            currentJobParams = jobParams;

            ForEachClient(client =>
            {
                if (client.Context.IsSubscribed)
                {
                    // check alive
                    var lastActivityAgo = clock.UtcNow - client.Context.LastActivity;

                    if (poolConfig.ClientConnectionTimeout > 0 &&
                        lastActivityAgo.TotalSeconds > poolConfig.ClientConnectionTimeout)
                    {
                        logger.Info(() => $"[{LogCat}] [{client.ConnectionId}] Booting zombie-worker (idle-timeout exceeded)");
                        DisconnectClient(client);
                        return;
                    }

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if (client.Context.ApplyPendingDifficulty())
                        client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(client.Context.Difficulty) });

                    // send job
                    client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        protected override ulong HashrateFromShares(IEnumerable<Tuple<object, IShare>> shares, int interval)
        {
            var sum = shares.Sum(share => Math.Max(0.00000001, share.Item2.Difficulty * manager.ShareMultiplier));
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = Math.Ceiling(((sum * multiplier / interval) / 1000000) * 2);
            return (ulong)result;
        }

        protected override void OnVarDiffUpdate(StratumClient<BitcoinWorkerContext> client, double newDiff)
        {
            client.Context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if (client.Context.HasPendingDifficulty)
            {
                client.Context.ApplyPendingDifficulty();

                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(client.Context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        private string EncodeTarget(double difficulty)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = ZCashConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray();
            var padded = Enumerable.Repeat((byte) 0, 32).ToArray();

            if (padded.Length - bytes.Length > 0)
            {
                Buffer.BlockCopy(bytes, 0, padded, padded.Length - bytes.Length, bytes.Length);
                bytes = padded;
            }

            var result = bytes.ToHexString();
            return result;
        }
    }
}
