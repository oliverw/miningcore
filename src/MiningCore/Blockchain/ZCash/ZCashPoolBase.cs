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
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.JsonRpc;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
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

        private ZCashCoinbaseTxConfig coinbaseTxConfig;
        private double hashrateDivisor;

        protected override BitcoinJobManager<TJob, ZCashBlockTemplate> CreateJobManager()
        {
            return ctx.Resolve<ZCashJobManager<TJob>>(
                new TypedParameter(typeof(IExtraNonceProvider), new ZCashExtraNonceProvider()));
        }

        #region Overrides of BitcoinPoolBase<TJob,ZCashBlockTemplate>

        /// <inheritdoc />
        protected override async Task SetupJobManager()
        {
            await base.SetupJobManager();

            if (ZCashConstants.CoinbaseTxConfig.TryGetValue(poolConfig.Coin.Type, out var coinbaseTx))
                coinbaseTx.TryGetValue(manager.NetworkType, out coinbaseTxConfig);

            hashrateDivisor = (double)new BigRational(coinbaseTxConfig.Diff1b,
                ZCashConstants.CoinbaseTxConfig[CoinType.ZEC][manager.NetworkType].Diff1b);
        }

        #endregion

        protected override void OnSubscribe(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

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
            context.IsSubscribed = true;
            context.UserAgent = requestParams?.Length > 0 ? requestParams[0].Trim() : null;
        }

        protected override async Task OnAuthorizeAsync(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            await base.OnAuthorizeAsync(client, tsRequest);

            var context = client.GetContextAs<BitcoinWorkerContext>();

            if (context.IsAuthorized)
            {
                // send intial update
                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        private void OnSuggestTarget(StratumClient client, Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;
            var context = client.GetContextAs<BitcoinWorkerContext>();

            if (request.Id == null)
            {
                client.RespondError(StratumError.Other, "missing request id", request.Id);
                return;
            }

            var requestParams = request.ParamsAs<string[]>();
            var target = requestParams.FirstOrDefault();

            if (!string.IsNullOrEmpty(target))
            {
                if (System.Numerics.BigInteger.TryParse(target, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var targetBig))
                {
                    var newDiff = (double) new BigRational(coinbaseTxConfig.Diff1b, targetBig);
                    var poolEndpoint = poolConfig.Ports[client.PoolEndpoint.Port];

                    if (newDiff >= poolEndpoint.Difficulty)
                    {
                        context.EnqueueNewDifficulty(newDiff);
                        context.ApplyPendingDifficulty();

                        client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                    }

                    else
                        client.RespondError(StratumError.Other, "suggested difficulty too low", request.Id);
                }

                else
                    client.RespondError(StratumError.Other, "invalid target", request.Id);
            }

            else
                client.RespondError(StratumError.Other, "invalid target", request.Id);
        }

        protected override async Task OnRequestAsync(StratumClient client,
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
                    OnSuggestTarget(client, tsRequest);
                    break;

                case BitcoinStratumMethods.ExtraNonceSubscribe:
                    // ignored
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

            logger.Info(() => $"[{LogCat}] Broadcasting job");

            ForEachClient(client =>
            {
                var context = client.GetContextAs<BitcoinWorkerContext>();

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

                    // varDiff: if the client has a pending difficulty change, apply it now
                    if (context.ApplyPendingDifficulty())
                        client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });

                    // send job
                    client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
                }
            });
        }

        public override double HashrateFromShares(double shares, double interval)
        {
            var multiplier = BitcoinConstants.Pow2x32 / manager.ShareMultiplier;
            var result = shares * multiplier / interval / 1000000 * 2;

            result /= hashrateDivisor;
            return result;
        }

        protected override void OnVarDiffUpdate(StratumClient client, double newDiff)
        {
            var context = client.GetContextAs<BitcoinWorkerContext>();

            context.EnqueueNewDifficulty(newDiff);

            // apply immediately and notify client
            if (context.HasPendingDifficulty)
            {
                context.ApplyPendingDifficulty();

                client.Notify(ZCashStratumMethods.SetTarget, new object[] { EncodeTarget(context.Difficulty) });
                client.Notify(BitcoinStratumMethods.MiningNotify, currentJobParams);
            }
        }

        private string EncodeTarget(double difficulty)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = coinbaseTxConfig.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray();
            var padded = ArrayPool<byte>.Shared.Rent(ZCashConstants.TargetPaddingLength);

            try
            {
                Array.Clear(padded, 0, ZCashConstants.TargetPaddingLength);
                var padLength = ZCashConstants.TargetPaddingLength - bytes.Length;

                if (padLength > 0)
                {
                    Array.Copy(bytes, 0, padded, padLength, bytes.Length);
                    bytes = padded;
                }

                var result = bytes.ToHexString(0, ZCashConstants.TargetPaddingLength);
                return result;
            }

            finally
            {
                ArrayPool<byte>.Shared.Return(padded);
            }
        }
    }
}
