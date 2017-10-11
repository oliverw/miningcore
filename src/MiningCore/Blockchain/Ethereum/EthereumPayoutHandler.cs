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
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Payments;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Util;
using Newtonsoft.Json;
using Contract = MiningCore.Contracts.Contract;
using EC = MiningCore.Blockchain.Ethereum.EthCommands;

namespace MiningCore.Blockchain.Ethereum
{
    [CoinMetadata(CoinType.ETH, CoinType.ETC)]
    public class EthereumPayoutHandler : PayoutHandlerBase,
        IPayoutHandler
    {
        public EthereumPayoutHandler(
            IComponentContext ctx,
            IConnectionFactory cf, 
            IMapper mapper,
            IShareRepository shareRepo,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo,
            NotificationService notificationService) :
            base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, notificationService)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

            this.ctx = ctx;
        }

        private readonly IComponentContext ctx;
        private DaemonClient daemon;
        private EthereumNetworkType networkType;
        private EthereumChainType chainType;

        protected override string LogCategory => "Ethereum Payout Handler";

        #region IPayoutHandler

        public void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
        {
            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;

            logger = LogUtil.GetPoolScopedLogger(typeof(EthereumPayoutHandler), poolConfig);

            // configure standard daemon
            var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

            var daemonEndpoints = poolConfig.Daemons
                .Where(x => string.IsNullOrEmpty(x.Category))
                .ToArray();

            daemon = new DaemonClient(jsonSerializerSettings);
            daemon.Configure(daemonEndpoints);
        }

        public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
        {
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(blocks, nameof(blocks));

            await DetectChainAsync();

            var pageSize = 100;
            var pageCount = (int)Math.Ceiling(blocks.Length / (double)pageSize);
            var result = new List<Block>();

            var immatureCount = 0;

            for (var i = 0; i < pageCount; i++)
            {
                // get a page full of blocks
                var page = blocks
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                // build command batch (block.TransactionConfirmationData is the hash of the blocks coinbase transaction)
                var batch = page.Select(block => new DaemonCmd(EC.GetBlockByNumber,
                    new[]
                    {
                        (object) block.BlockHeight.ToStringHexWithPrefix(),
                        true
                    })).ToArray();

                // execute batch
                var results = await daemon.ExecuteBatchAnyAsync(batch);

                for (var j = 0; j < results.Length; j++)
                {
                    var cmdResult = results[j];

                    var blockInfo = cmdResult.Response?.ToObject<DaemonResponses.Block>();
                    var block = page[j];

                    // extract confirmation data from stored block
                    var mixHash = block.TransactionConfirmationData.Split(":").First();
                    var nonce = block.TransactionConfirmationData.Split(":").LastOrDefault();

                    // check error
                    if (cmdResult.Error != null)
                    {
                        logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
                    }

                    // missing details are interpreted as "orphaned"
                    else if (blockInfo == null)
                    {
                        block.Status = BlockStatus.Orphaned;
                        result.Add(block);
                    }

                    else if (blockInfo.Miner != poolConfig.Address)
                    {
                        block.Status = BlockStatus.Orphaned;
                        result.Add(block);
                    }

                    else
                    {
                        // additional check
                        var match = blockInfo.SealFields[0] == mixHash && blockInfo.Nonce == nonce;

                        block.Status = BlockStatus.Confirmed;
                        block.Reward = GetBaseBlockReward(block.BlockHeight);   // base reward
                        block.Reward += blockInfo.Uncles.Length * (block.Reward / 32); // uncle rewards
                        block.Reward += GetTxReward(blockInfo); // tx fees

                        result.Add(block);
                    }
                }
            }

            return result.ToArray();
        }

        public Task UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
        {
            return Task.FromResult(true);
        }

        public Task PayoutAsync(Balance[] balances)
        {
            return Task.FromResult(true);
        }

        #endregion // IPayoutHandler

        private decimal GetBaseBlockReward(ulong height)
        {
            if (height >= EthereumConstants.ByzantiumHardForkHeight)
                return EthereumConstants.ByzantiumBlockReward;

            return EthereumConstants.HomesteadBlockReward;
        }

        private decimal GetTxReward(DaemonResponses.Block blockInfo)
        {
            var result = blockInfo.Transactions.Sum(x => ((decimal) x.Gas / EthereumConstants.Wei) *
                                                         ((decimal) x.GasPrice / EthereumConstants.Wei));

            return result;
        }

        private async Task DetectChainAsync()
        {
            var commands = new[]
            {
                new DaemonCmd(EC.GetNetVersion),
                new DaemonCmd(EC.ParityChain),
            };

            var results = await daemon.ExecuteBatchAnyAsync(commands);

            if (results.Any(x => x.Error != null))
            {
                var errors = results.Where(x => x.Error != null)
                    .ToArray();

                if (errors.Any())
                    throw new Exception($"Chain detection failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
            }

            // convert network
            var netVersion = results[0].Response.ToObject<string>();
            var parityChain = results[1].Response.ToObject<string>();

            EthereumUtils.DetectNetworkAndChain(netVersion, parityChain, out networkType, out chainType);
        }
    }
}
