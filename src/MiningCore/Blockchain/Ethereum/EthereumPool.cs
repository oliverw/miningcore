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
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using AutoMapper;
using MiningCore.Configuration;
using MiningCore.JsonRpc;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Stratum;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC)]
    public class EthereumPool : PoolBase<EthereumWorkerContext>
    {
        public EthereumPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, notificationSenders)
        {
        }

        private long currentJobId;

        private EthereumJobManager manager;
        private static readonly TimeSpan maxShareAge = TimeSpan.FromSeconds(5);

        private string NextJobId()
        {
            return Interlocked.Increment(ref currentJobId).ToString(CultureInfo.InvariantCulture);
        }

        private void OnNewJob()
        {
        }

        #region Overrides

        protected override async Task SetupJobManager()
        {
            manager = ctx.Resolve<EthereumJobManager>();
            manager.Configure(poolConfig, clusterConfig);

            await manager.StartAsync();

            disposables.Add(manager.Blocks.Subscribe(_ => OnNewJob()));

            // we need work before opening the gates
            await manager.Blocks.Take(1).ToTask();
        }

        protected override async Task OnRequestAsync(StratumClient<EthereumWorkerContext> client,
            Timestamped<JsonRpcRequest> tsRequest)
        {
            var request = tsRequest.Value;

            switch (request.Method)
            {
                default:
                    logger.Debug(() => $"[{LogCat}] [{client.ConnectionId}] Unsupported RPC request: {JsonConvert.SerializeObject(request, serializerSettings)}");

                    client.RespondError(StratumError.Other, $"Unsupported request {request.Method}", request.Id);
                    break;
            }
        }

        protected override void SetupStats()
        {
            base.SetupStats();

            // Pool Hashrate
            var poolHashRateSampleIntervalSeconds = 60 * 10;

            disposables.Add(validSharesSubject
                .Buffer(TimeSpan.FromSeconds(poolHashRateSampleIntervalSeconds))
                .Do(shares => UpdateMinerHashrates(shares, poolHashRateSampleIntervalSeconds))
                .Select(shares =>
                {
                    if (!shares.Any())
                        return 0ul;

                    try
                    {
                        return HashrateFromShares(shares, poolHashRateSampleIntervalSeconds);
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                        return 0ul;
                    }
                })
                .Subscribe(hashRate => poolStats.PoolHashRate = hashRate));
        }

        protected override ulong HashrateFromShares(IEnumerable<IShare> shares, int interval)
        {
            var result = Math.Ceiling(shares.Sum(share => share.StratumDifficulty) / interval);
            return (ulong)result;
        }

        protected override void UpdateVarDiffAndNotifyClient(StratumClient<EthereumWorkerContext> client)
        {
            //UpdateVarDiff(client, manager.BlockchainStats.NetworkDifficulty);

            //if (client.Context.ApplyPendingDifficulty())
            //{
            //    // send job
            //    var job = CreateWorkerJob(client);
            //    client.Notify(MoneroStratumMethods.JobNotify, job);
            //}
        }

        protected override async Task UpdateBlockChainStatsAsync()
        {
            await manager.UpdateNetworkStatsAsync();

            blockchainStats = manager.BlockchainStats;
        }

        #endregion // Overrides
    }
}
