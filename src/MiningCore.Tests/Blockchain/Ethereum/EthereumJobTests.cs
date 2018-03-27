using System.IO;
using System.Threading.Tasks;
using MiningCore.Blockchain.Ethereum;
using MiningCore.Crypto.Hashing.Ethash;
using MiningCore.Stratum;
using Newtonsoft.Json;
using Xunit;

namespace MiningCore.Tests.Blockchain.Ethereum
{
	/// <summary>
	/// These tests take ages to complete (> 10 min. on modern hardware)
	/// due to the time it takes to generate the DAG for EthashFull
	/// </summary>
	public class EthereumJobTests : TestBase
    {
        static readonly EthashFull ethash = new EthashFull(3, Path.GetTempPath());

		/*
        [Fact]
        public async Task EthereumJob_Should_Accept_Valid_Share()
        {
            var worker = new StratumClient();

			worker.SetContext(new EthereumWorkerContext
			{
				Difficulty = 0.05,
				ExtraNonce1 = "0003",
			});

            var bt = JsonConvert.DeserializeObject<EthereumBlockTemplate>(
                "{\"Height\":1939983,\"Header\":\"0x2b1dc55864dc6785ae5694b2a5c8fb301fee511262385007b69c06f6940f0ee3\",\"Seed\":\"0x4220f7b47dc9e1f91e2d7c117a12e9158ce7a78185c805d21338759838f6f55d\",\"Target\":5547597442670231301113464668059123372289016956050714228028458602362,\"ParentHash\":\"0x372c7825b893281707dfc7676321fb237eef771fef724f70847c8085005f7627\",\"Difficulty\":20872475055}");

            var job = new EthereumJob("00000062", bt);
            var share = await job.ProcessShareAsync(worker, "000009f3003a", ethash);

            Assert.NotNull(share);
            Assert.True(share.IsBlockCandidate);
            Assert.Equal(share.TransactionConfirmationData, "0xbaa8dfa217eca2af764164cbb2e1112663ffac64940cc237d8a884500f62b3a2:0x0003000009f3003a");
            Assert.Equal(share.BlockHeight, 1939983);
            Assert.Equal(share.Difficulty, 214748364.8);
        }

        [Fact]
        public async Task EthereumJob_Should_Not_Accept_Invalid_Share()
        {
	        var worker = new StratumClient();

	        worker.SetContext(new EthereumWorkerContext
	        {
		        Difficulty = 0.05,
		        ExtraNonce1 = "0003",
	        });

            var bt = JsonConvert.DeserializeObject<EthereumBlockTemplate>(
                "{\"Height\":1939983,\"Header\":\"0x2b1dc55864dc6785ae5694b2a5c8fb301fee511262385007b69c06f6940f0ee3\",\"Seed\":\"0x4220f7b47dc9e1f91e2d7c117a12e9158ce7a78185c805d21338759838f6f55d\",\"Target\":5547597442670231301113464668059123372289016956050714228028458602362,\"ParentHash\":\"0x372c7825b893281707dfc7676321fb237eef771fef724f70847c8085005f7627\",\"Difficulty\":20872475055}");

            var job = new EthereumJob("00000062", bt);

            // invalid nonce
            await Assert.ThrowsAsync<StratumException>(() => job.ProcessShareAsync(worker, "00000af3003a", ethash));

            // invalid extra-nonce
            worker.GetContextAs<EthereumWorkerContext>().ExtraNonce1 = "0001";
            await Assert.ThrowsAsync<StratumException>(() => job.ProcessShareAsync(worker, "000009f3003a", ethash));
        }
		*/
    }
}
