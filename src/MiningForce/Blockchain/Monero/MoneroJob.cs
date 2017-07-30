using System;
using System.Collections.Generic;
using System.Globalization;
using CodeContracts;
using MiningForce.Blockchain.Monero.DaemonResponses;
using MiningForce.Configuration;
using MiningForce.Stratum;

namespace MiningForce.Blockchain.Monero
{
	public class MoneroJob
	{
		public MoneroJob(GetBlockTemplateResponse blockTemplate, string jobId,
			PoolConfig poolConfig, ClusterConfig clusterConfig,
			MoneroNetworkType networkType,
			ExtraNonceProvider extraNonceProvider)
		{
			Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

			//blockTemplate = JsonConvert.DeserializeObject<GetBlockTemplateResult>("{\"capabilities\":[\"proposal\"],\"version\":536870912,\"rules\":[\"csv\",\"segwit\"],\"vbavailable\":{},\"vbrequired\":0,\"previousblockhash\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac4\",\"transactions\":[{\"data\":\"0100000001f209942b305d7e1ff4d4017f8e4a82946e35ab943d98049580a5ef446741c07f010000006a47304402201735be63bf6527f3a419eccd8dddd2a68bd652170a304b95a1f76e3a9a310908022044c86f2e3238aa8ec960206d5395fcbe69fa61a2671895fda2142474d01ecf280121039be344e1dc60c08355f4ba7cd3c7e0a57873aba19e782d281143a4ada0481663feffffff02002d3101000000001976a9147b108ab6bbffc56a6c1ce0d14edad1a588aac9f088ac5610ba17000000001976a9146f63066c9f43becbd89a2c5ef32268ef07b8907b88ac68ed0100\",\"txid\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"hash\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"depends\":[],\"fee\":22600,\"sigops\":8,\"weight\":900},{\"data\":\"01000000020749c01b88bbb998ea2bb829d2fd1509e064a865be25584ebb7d238c2d2c7ce6000000006b483045022100e1a30aec16addf31111acb977dfbbe8747a43b4fd810a6c53a4622be409e8921022024273c724935cf00bece0e07bdf48e2ca2144cd45805689b2ca801fbbe919bb20121031b392bcd589e62244a4d1529f1e21aba9defb0ffa71c5e2e14e22f3f5081e00afeffffffc6fba4e75b294a488c62b41bc02ebcfb9d33c1842960ef2c456676112cc134c7000000006b483045022100df744f016524b15a73d3f603613a4f8cb58bf082a10c1602d0a0f6f8c06de71a0220134babf4b34582d3fbcfbf0b70092a995c356c66053b767a6bfd684537ba66760121033a6da8e5dd47967df138f6d32d834571db1b90c5f4f531683078550aa98038dafeffffff02f3821100000000001976a914b52631c6125289fe5d59319c39c149aac2bc8b9b88ac40b9ec05000000001976a9145ea4e971f6007878ee2215ac3dbadae13b41585988ac68ed0100\",\"txid\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"hash\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"depends\":[],\"fee\":37400,\"sigops\":8,\"weight\":1496}],\"coinbaseaux\":{\"flags\":\"\"},\"coinbasevalue\":5000060000,\"longpollid\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac455260\",\"target\":\"0000003713130000000000000000000000000000000000000000000000000000\",\"mintime\":1501243676,\"mutable\":[\"time\",\"transactions\",\"prevblock\"],\"noncerange\":\"00000000ffffffff\",\"sigoplimit\":80000,\"sizelimit\":4000000,\"weightlimit\":4000000,\"curtime\":1501245752,\"bits\":\"1d371313\",\"height\":126313,\"default_witness_commitment\":\"6a24aa21a9edc0669f40a7281cb4f8c88e35f084319af5408fe75fba1458c5ccf871a984c887\"}");

			this.poolConfig = poolConfig;
			this.clusterConfig = clusterConfig;
			this.networkType = networkType;
			this.blockTemplate = blockTemplate;
			this.jobId = jobId;
		}

		private readonly string jobId;
		private readonly ClusterConfig clusterConfig;
		private readonly PoolConfig poolConfig;
		private readonly GetBlockTemplateResponse blockTemplate;
		private readonly HashSet<string> submissions = new HashSet<string>();
		private readonly MoneroNetworkType networkType;

		#region API-Surface

		public GetBlockTemplateResponse BlockTemplate => blockTemplate;
		public string JobId => jobId;

		public void Init()
		{
		}

		public object GetJobParams(bool isNew)
		{
			return new object[]
			{
				jobId,
				isNew
			};
		}

		public ShareBase ProcessShare(string extraNonce1, string extraNonce2, string nTime, string nonce, double stratumDifficulty)
		{
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce1), $"{nameof(extraNonce1)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

			// validate nonce
			if (nonce.Length != 8)
				throw new StratumException(StratumError.Other, "incorrect size of nonce");

			var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

			// check for dupes
			if (!RegisterSubmit(extraNonce1, extraNonce2, nTime, nonce))
				throw new StratumException(StratumError.DuplicateShare, "duplicate share");

			return ProcessShareInternal(extraNonce1, extraNonce2, nonceInt, stratumDifficulty);
		}

		#endregion // API-Surface

		private bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
		{
			var key = extraNonce1 + extraNonce2 + nTime + nonce;
			if (submissions.Contains(key))
				return false;

			submissions.Add(key);
			return true;
		}

		private ShareBase ProcessShareInternal(string extraNonce1, string extraNonce2, uint nonce, double stratumDifficulty)
		{
			// valid share
			var result = new ShareBase
			{
			};

			return result;
		}
	}
}
