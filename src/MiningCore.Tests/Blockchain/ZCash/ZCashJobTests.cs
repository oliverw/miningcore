using System;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.ZCash;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Special;
using MiningCore.Stratum;
using MiningCore.Tests.Util;
using NBitcoin;
using Newtonsoft.Json;
using Xunit;

namespace MiningCore.Tests.Blockchain.ZCash
{
    public class ZCashJobTests : TestBase
    {
        readonly PoolConfig poolConfig = new PoolConfig
        {
            Coin = new CoinConfig { Type = CoinType.ZEC }
        };

        readonly ClusterConfig clusterConfig = new ClusterConfig();
        private readonly IDestination poolAddressDestination = BitcoinUtils.AddressToDestination("tmUEUSYYGQY3G5KMNkxAqkYYNfstaCsRCM5");

        protected readonly IHashAlgorithm sha256d = new Sha256D();
        protected readonly IHashAlgorithm sha256dReverse = new DigestReverser(new Sha256D());

        [Fact]
        public void ZCashJob_Testnet_Validate_FoundersRewardAddress_At_Height()
        {
            var job = new ZCashJob();

            var bt = new ZCashBlockTemplate
            {
                Target = "0000407f43000000000000000000000000000000000000000000000000000000",
                PreviousBlockhash = "000003be5873fc64b1b784318e3226a1ab2a1805bebba5a0d670be54ff7772e8",
                Bits = "003355",
                Transactions = new BitcoinBlockTransaction[0],
                Subsidy = new ZCashBlockSubsidy
                {
                    Founders = 2.5m,
                    Miner = 10m,
                }
            };

            var clock = new MockMasterClock { CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869874).UtcDateTime };

            job.Init(bt, "1", poolConfig, clusterConfig, clock, poolAddressDestination, BitcoinNetworkType.Test,
                false, 1, 1, sha256d, sha256d, sha256dReverse);

            bt.Height = 1;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2UNzUUx8mWBCRYPRezvA363EYXyEpHokyi");
            bt.Height = 53126;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2NGQjYMQhFndDHguvUw4wZdNdsssA6K7x2");
            bt.Height = 53127;
            Assert.Equal(job.GetFoundersRewardAddress(), "t2ENg7hHVqqs9JwU5cgjvSbxnT2a9USNfhy");
        }
    }
}
