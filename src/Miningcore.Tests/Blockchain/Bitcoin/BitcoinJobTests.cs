using System.Threading.Tasks;
using Miningcore.Blockchain.Bitcoin;
using Xunit;
#pragma warning disable 8974

namespace Miningcore.Tests.Blockchain.Bitcoin;

public class BitcoinJobTests : TestBase
{
    [Fact]
    public void Process_Valid_Share()
    {
        var job = new BitcoinJob();

        // job.Init(blockTemplate, NextJobId(),
        //     poolConfig, extraPoolConfig, clusterConfig, clock, poolAddressDestination, network, isPoS,
        //     ShareMultiplier, coin.CoinbaseHasherValue, coin.HeaderHasherValue,
        //     !isPoS ? coin.BlockHasherValue : coin.PoSBlockHasherValue ?? coin.BlockHasherValue);
    }
}
