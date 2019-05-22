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
using System.Linq;
using Miningcore.Extensions;
using NBitcoin;
using Transaction = NBitcoin.Transaction;

namespace Miningcore.Blockchain.Equihash.Custom.Minexcoin
{
    public class MinexcoinJob : EquihashJob
    {
        private static readonly Script bankScript = new Script("2103ae6efe9458f1d3bdd9a458b1970eabbdf9fcb1357e0dff2744a777ff43c391eeac".HexToByteArray());
        private const decimal BlockReward = 250000000m;  // Minexcoin has a static block reward

        protected override Transaction CreateOutputTransaction()
        {
            var txFees = BlockTemplate.Transactions.Sum(x => x.Fee);
            rewardToPool = new Money(BlockReward + txFees, MoneyUnit.Satoshi);

            var bankReward = ComputeBankReward(BlockTemplate.Height, rewardToPool);
            rewardToPool -= bankReward;

            var tx = Transaction.Create(network);

            // pool reward
            tx.Outputs.Add(rewardToPool, poolAddressDestination);

            // bank reward
            tx.Outputs.Add(bankReward, bankScript);

            tx.Inputs.Add(TxIn.CreateCoinbase((int) BlockTemplate.Height));

            return tx;
        }

        private Money ComputeBankReward(uint blockHeight, Money totalReward)
        {
            if(blockHeight <= 4500000)
            {
                /**
                 *       1- 900000 20%
                 *  900001-1800000 30%
                 * 1800001-2700000 40%
                 * 2700001-3600000 50%
                 * 3600001-4500000 60%
                 */
                return new Money(Math.Floor((decimal) totalReward.Satoshi / 10) * (2.0m + Math.Floor(((decimal) blockHeight - 1) / 900000)), MoneyUnit.Satoshi);
            }

            // 70%
            return new Money(Math.Floor((decimal) totalReward.Satoshi / 10) * 7, MoneyUnit.Satoshi);
        }
    }
}
