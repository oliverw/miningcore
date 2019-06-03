using System;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Tests.Util;
using NBitcoin;
using Newtonsoft.Json;
using Xunit;

namespace Miningcore.Tests.Blockchain.Bitcoin
{
    public class BitcoinJobTests : TestBase
    {
        public BitcoinJobTests()
        {
            poolConfig = new PoolConfig
            {
                Coin = "bitcoin",
                Template = ModuleInitializer.CoinTemplates["bitcoin"]
            };
        }

        readonly PoolConfig poolConfig;

        readonly ClusterConfig clusterConfig = new ClusterConfig();
        private readonly IDestination poolAddressDestination = BitcoinUtils.AddressToDestination("mjn3q42yxr9yLA3gyseHCZCHEptZC31PEh", Network.RegTest);

        protected readonly IHashAlgorithm sha256d = new Sha256D();
        protected readonly IHashAlgorithm sha256dReverse = new DigestReverser(new Sha256D());

        [Fact]
        public void BitcoinJob_Should_Accept_Valid_Share()
        {
            var worker = new StratumClient();

            worker.SetContext(new BitcoinWorkerContext
            {
                Difficulty = 0.5,
                ExtraNonce1 = "01000058",
            });

            var bt = JsonConvert.DeserializeObject<Miningcore.Blockchain.Bitcoin.DaemonResponses.BlockTemplate>(
                "{\"Version\":536870912,\"PreviousBlockhash\":\"000000000909578519b5be7b37fdc53b2923817921c43108a907b72264da76bb\",\"CoinbaseValue\":5000000000,\"Target\":\"7fffff0000000000000000000000000000000000000000000000000000000000\",\"NonceRange\":\"00000000ffffffff\",\"CurTime\":1508869874,\"Bits\":\"207fffff\",\"Height\":14,\"Transactions\":[],\"CoinbaseAux\":{\"Flags\":\"0b2f454231362f414431322f\"},\"default_witness_commitment\":null}");

            var job = new BitcoinJob();

            // set clock to job creation time
            var clock = new MockMasterClock { CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869874).UtcDateTime };

            job.Init(bt, "1", poolConfig, null, clusterConfig, clock, poolAddressDestination, Network.RegTest,
                false, 1, sha256d, sha256d, sha256dReverse);

            // set clock to submission time
            clock.CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869907).UtcDateTime;

            var (share, blockHex) = job.ProcessShare(worker, "01000000", "59ef86f2", "8d84ae6a");

            Assert.NotNull(share);
            Assert.True(share.IsBlockCandidate);
            Assert.Equal(share.BlockHash, "601ed85039804bcecbbdb53e0ca358aeb8dabef2366fb64c216aac3aba02b716");
            Assert.Equal(blockHex, "00000020bb76da6422b707a90831c421798123293bc5fd377bbeb5198557090900000000fd5418fe788ef961678e4bacdd1fe3903185b9ec63865bb3d2d279bb0eb48c0bf286ef59ffff7f206aae848d0101000000010000000000000000000000000000000000000000000000000000000000000000ffffffff295e0c0b2f454231362f414431322f04f286ef590001000058010000000c2f4d696e696e67436f72652f000000000100f2052a010000001976a9142ebb5cccf9a6bb927661d2953655c43c04accc3788ac00000000");
            Assert.Equal(share.BlockHeight, 14);
            Assert.Equal(share.Difficulty, 0.5);
        }

        [Fact]
        public void BitcoinJob_Should_Not_Accept_Invalid_Share()
        {
            var worker = new StratumClient();

            worker.SetContext(new BitcoinWorkerContext
            {
                Difficulty = 0.5,
                ExtraNonce1 = "01000058",
            });

            var bt = JsonConvert.DeserializeObject<Miningcore.Blockchain.Bitcoin.DaemonResponses.BlockTemplate>(
                "{\"Version\":536870912,\"PreviousBlockhash\":\"000000000909578519b5be7b37fdc53b2923817921c43108a907b72264da76bb\",\"CoinbaseValue\":5000000000,\"Target\":\"7fffff0000000000000000000000000000000000000000000000000000000000\",\"NonceRange\":\"00000000ffffffff\",\"CurTime\":1508869874,\"Bits\":\"207fffff\",\"Height\":14,\"Transactions\":[],\"CoinbaseAux\":{\"Flags\":\"0b2f454231362f414431322f\"},\"default_witness_commitment\":null}");

            var job = new BitcoinJob();

            // set clock to job creation time
            var clock = new MockMasterClock { CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869874).UtcDateTime };

            job.Init(bt, "1", poolConfig, null, clusterConfig, clock, poolAddressDestination, Network.RegTest,
                false, 1, sha256d, sha256d, sha256dReverse);

            // set clock to submission time
            clock.CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1508869907).UtcDateTime;

            // invalid extra-nonce 2
            Assert.Throws<StratumException>(() => job.ProcessShare(worker, "02000000", "59ef86f2", "8d84ae6a"));

            // make sure we don't accept case-sensitive duplicate shares as basically 0xdeadbeaf = 0xDEADBEAF.
            var (share, blockHex) = job.ProcessShare(worker, "01000000", "59ef86f2", "8d84ae6a");
            Assert.Throws<StratumException>(() => job.ProcessShare(worker, "01000000", "59ef86f2", "8D84AE6A"));

            // invalid time
            Assert.Throws<StratumException>(() => job.ProcessShare(worker, "01000000", "69ef86f2", "8d84ae6a"));

            // invalid nonce
            Assert.Throws<StratumException>(() => job.ProcessShare(worker, "01000000", "59ef86f2", "4a84be6a"));

            // valid share data but invalid submission time
            clock.CurrentTime = DateTimeOffset.FromUnixTimeSeconds(1408869907).UtcDateTime;
            Assert.Throws<StratumException>(() => job.ProcessShare(worker, "01000000", "59ef86f2", "8d84ae6a"));
        }
    }
}
