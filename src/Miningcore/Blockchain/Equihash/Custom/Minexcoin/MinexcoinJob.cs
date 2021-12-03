using Miningcore.Extensions;
using NBitcoin;
using Transaction = NBitcoin.Transaction;

namespace Miningcore.Blockchain.Equihash.Custom.Minexcoin;

public class MinexcoinJob : EquihashJob
{
    private static readonly Script bankScript = new("2103ae6efe9458f1d3bdd9a458b1970eabbdf9fcb1357e0dff2744a777ff43c391eeac".HexToByteArray());
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
