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
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Configuration;
using NBitcoin;

namespace MiningCore.Blockchain.Dash
{
    public class DashJob : BitcoinJob<DaemonResponses.DashBlockTemplate>
    {
        protected override Transaction CreateOutputTransaction()
        {
            var blockReward = new Money(BlockTemplate.CoinbaseValue * blockRewardMultiplier, MoneyUnit.Satoshi);
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = new Transaction();
            rewardToPool = CreateDashOutputs(tx, blockReward);

            // Finally distribute remaining funds to pool
            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination)
            {
                Value = rewardToPool
            });

            return tx;
        }

        private Money CreateDashOutputs(Transaction tx, Money reward)
        {
            if (BlockTemplate.Masternode != null && BlockTemplate.SuperBlocks != null)
            {
                if (!string.IsNullOrEmpty(BlockTemplate.Masternode.Payee))
                {
                    var payeeAddress = BitcoinUtils.AddressToDestination(BlockTemplate.Masternode.Payee);
                    var payeeReward = BlockTemplate.Masternode.Amount;

                    reward -= payeeReward;
                    rewardToPool -= payeeReward;

                    tx.AddOutput(payeeReward, payeeAddress);
                }

                else if (BlockTemplate.SuperBlocks.Length > 0)
                {
                    foreach(var superBlock in BlockTemplate.SuperBlocks)
                    {
                        var payeeAddress = BitcoinUtils.AddressToDestination(superBlock.Payee);
                        var payeeReward = superBlock.Amount;

                        reward -= payeeReward;
                        rewardToPool -= payeeReward;

                        tx.AddOutput(payeeReward, payeeAddress);
                    }
                }
            }

            if (!string.IsNullOrEmpty(BlockTemplate.Payee))
            {
                var payeeAddress = BitcoinUtils.AddressToDestination(BlockTemplate.Payee);
                var payeeReward = BlockTemplate.PayeeAmount ?? (reward / 5);

                reward -= payeeReward;
                rewardToPool -= payeeReward;

                tx.AddOutput(payeeReward, payeeAddress);
            }

            return reward;
        }
    }
}
