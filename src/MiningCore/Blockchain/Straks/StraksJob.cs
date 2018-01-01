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
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Straks.DaemonResponses;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace MiningCore.Blockchain.Straks
{
    public class StraksJob : BitcoinJob<StraksBlockTemplate>
    {
        protected override Transaction CreateOutputTransaction()
        {
            var blockReward = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = new Transaction();
            rewardToPool = CreateStraksOutputs(tx, blockReward);

            // Finally distribute remaining funds to pool
            tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination)
            {
                Value = rewardToPool
            });

            return tx;
        }
        private bool ShouldHandleMasternodePayment()
        {
            return BlockTemplate.MasternodePaymentsStarted &&
            BlockTemplate.MasternodePaymentsEnforced &&
            !string.IsNullOrEmpty(BlockTemplate.Payee) && BlockTemplate.PayeeAmount.HasValue;
        }

        private Money CreateStraksOutputs(Transaction tx, Money reward)
        {
            var treasuryRewardAddress = GetTreasuryRewardAddress();

            if (reward > 0 && treasuryRewardAddress != null)
            {
                var destination = TreasuryAddressToScriptDestination(treasuryRewardAddress);
                var treasuryReward = new Money(BlockTemplate.CoinbaseTx.TreasuryReward, MoneyUnit.Satoshi);
                tx.AddOutput(treasuryReward, destination);
                reward -= treasuryReward;
            }

            if (ShouldHandleMasternodePayment())
            {
                var payeeAddress = BitcoinUtils.AddressToDestination(BlockTemplate.Payee);
                var payeeReward = BlockTemplate.PayeeAmount.Value;

                reward -= payeeReward;
                rewardToPool -= payeeReward;

                tx.AddOutput(payeeReward, payeeAddress);
            }

            return reward;
        }

        public string GetTreasuryRewardAddress()
        {
            if (poolConfig.Extra != null && poolConfig.Extra.ContainsKey("treasuryAddresses"))
            {
                var addresses = poolConfig.Extra["treasuryAddresses"] as JArray;
                if (addresses.Count > 0)
                {
                    var index = Convert.ToInt32(BlockTemplate.Height % addresses.Count);
                    return addresses[index].ToObject<string>();
                }
            }
            return null;
        }

        public static IDestination TreasuryAddressToScriptDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(1).Take(20).ToArray();
            var result = new ScriptId(hash);
            return result;
        }
    }
}
