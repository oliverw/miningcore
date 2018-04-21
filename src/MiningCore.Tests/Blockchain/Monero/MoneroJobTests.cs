using MiningCore.Blockchain.Monero;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Stratum;
using Newtonsoft.Json;
using Xunit;

namespace MiningCore.Tests.Blockchain.Monero
{
    public class BitcoinJobTests : TestBase
    {
        readonly PoolConfig poolConfig = new PoolConfig { Coin = new CoinConfig {Type = CoinType.XMR}};
        readonly ClusterConfig clusterConfig = new ClusterConfig();

        [Fact]
        public void MoneroJob_Should_Accept_Valid_Share()
        {
	        var worker = new StratumClient();

	        worker.SetContext(new MoneroWorkerContext
			{
				Difficulty = 1000,
	        });

            var bt = JsonConvert.DeserializeObject<MiningCore.Blockchain.Monero.DaemonResponses.GetBlockTemplateResponse>(
                "{\"blocktemplate_blob\":\"0106e7eabdcf058234351e2e6ea901a56b33bb531587424321873072d80a9e97295b6c43152b9d00000000019c0201ffe00106e3a1a0cc010275d92c0a057aa5f073079694a153d426f837f49fdb9654da10a5364e79a2086280a0d9e61d028b46dca0d04998500b40b046fd6f8bb33229e6380fd465dbb1327aa6f813d8bd80c0fc82aa0202372f076459e769116d604d30aabff7160782acc0d20e0c5cdc8963ed4e16372f8090cad2c60e02f009504ce65538bbb684b466b21be3a90e3740f185d7089d37b75f0cf62b6e7680e08d84ddcb0102cf01b85c0b592bb6e508e20b5d317052b75de121908390363201abff3476ef0180c0caf384a302024b81076c8ad0cfe84cc32fe0813d63cdd0f7d8d0e56d82aa3f58cbbe49d4c61e2b017aaf3074be7ecb30a769595758e4da7c7c87ead864baf89b679b73153dfa352c0208000000000000000000\",\"Difficulty\":2,\"Height\":224,\"prev_hash\":\"8234351e2e6ea901a56b33bb531587424321873072d80a9e97295b6c43152b9d\",\"reserved_offset\":322,\"Status\":\"OK\"}");

            var job = new MoneroJob(bt, "d150da".HexToByteArray(), "1", poolConfig, clusterConfig);
            var (share, blobHex, blobHash) = job.ProcessShare("040100a4", 1, "f29c7fbf57d97eeedb61555857d7a34314250da20742b8157f96e0be89530a00", worker);

            Assert.NotNull(share);
            Assert.True(share.IsBlockCandidate);
            Assert.Equal(blobHash, "9258faf2dff5daf026681b5fa5d94a34dbb5bade1d9e2070865ba8c68f8f0454");
            Assert.Equal(blobHex, "0106e7eabdcf058234351e2e6ea901a56b33bb531587424321873072d80a9e97295b6c43152b9d040100a4019c0201ffe00106e3a1a0cc010275d92c0a057aa5f073079694a153d426f837f49fdb9654da10a5364e79a2086280a0d9e61d028b46dca0d04998500b40b046fd6f8bb33229e6380fd465dbb1327aa6f813d8bd80c0fc82aa0202372f076459e769116d604d30aabff7160782acc0d20e0c5cdc8963ed4e16372f8090cad2c60e02f009504ce65538bbb684b466b21be3a90e3740f185d7089d37b75f0cf62b6e7680e08d84ddcb0102cf01b85c0b592bb6e508e20b5d317052b75de121908390363201abff3476ef0180c0caf384a302024b81076c8ad0cfe84cc32fe0813d63cdd0f7d8d0e56d82aa3f58cbbe49d4c61e2b017aaf3074be7ecb30a769595758e4da7c7c87ead864baf89b679b73153dfa352c02080000000001d150da00");
            Assert.Equal(share.BlockHeight, 224);
            Assert.Equal(share.Difficulty, 1000);
        }

        [Fact]
        public void MoneroJob_Should_Not_Accept_Invalid_Share()
        {
	        var worker = new StratumClient();

	        worker.SetContext(new MoneroWorkerContext
	        {
		        Difficulty = 1000,
	        });

			var bt = JsonConvert.DeserializeObject<MiningCore.Blockchain.Monero.DaemonResponses.GetBlockTemplateResponse>(
                "{\"blocktemplate_blob\":\"0106e7eabdcf058234351e2e6ea901a56b33bb531587424321873072d80a9e97295b6c43152b9d00000000019c0201ffe00106e3a1a0cc010275d92c0a057aa5f073079694a153d426f837f49fdb9654da10a5364e79a2086280a0d9e61d028b46dca0d04998500b40b046fd6f8bb33229e6380fd465dbb1327aa6f813d8bd80c0fc82aa0202372f076459e769116d604d30aabff7160782acc0d20e0c5cdc8963ed4e16372f8090cad2c60e02f009504ce65538bbb684b466b21be3a90e3740f185d7089d37b75f0cf62b6e7680e08d84ddcb0102cf01b85c0b592bb6e508e20b5d317052b75de121908390363201abff3476ef0180c0caf384a302024b81076c8ad0cfe84cc32fe0813d63cdd0f7d8d0e56d82aa3f58cbbe49d4c61e2b017aaf3074be7ecb30a769595758e4da7c7c87ead864baf89b679b73153dfa352c0208000000000000000000\",\"Difficulty\":2,\"Height\":224,\"prev_hash\":\"8234351e2e6ea901a56b33bb531587424321873072d80a9e97295b6c43152b9d\",\"reserved_offset\":322,\"Status\":\"OK\"}");

            var job = new MoneroJob(bt, "d150da".HexToByteArray(), "1", poolConfig, clusterConfig);

            // invalid nonce
            Assert.Throws<StratumException>(() =>
                job.ProcessShare("040100a5", 1, "f29c7fbf57d97eeedb61555857d7a34314250da20742b8157f96e0be89530a00", worker));

            // invalid extra-nonce
            Assert.Throws<StratumException>(() =>
                job.ProcessShare("040100a4", 2, "f29c7fbf57d97eeedb61555857d7a34314250da20742b8157f96e0be89530a00", worker));

            // invalid hash
            Assert.Throws<StratumException>(() =>
                job.ProcessShare("040100a4", 1, "a29c7fbf57d97eeedb61555857d7a34314250da20742b8157f96e0be89530a00", worker));

            // invalid hash 2
            Assert.Throws<StratumException>(() =>
                job.ProcessShare("040100a4", 1, "9c7fbf57d97eeedb61555857d7a34314250da20742b8157f96e0be89530a00", worker));
        }
    }
}
