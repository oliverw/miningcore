using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CodeContracts;
using MiningForce.Blockchain.Bitcoin.DaemonResults;
using MiningForce.Configuration;
using MiningForce.Crypto;
using MiningForce.Extensions;
using MiningForce.Stratum;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = NBitcoin.Transaction;

namespace MiningForce.Blockchain.Bitcoin
{
	public class BitcoinJob
	{
		public BitcoinJob(GetBlockTemplateResult blockTemplate, string jobId,
			PoolConfig poolConfig, ClusterConfig clusterConfig,
			IDestination poolAddressDestination, BitcoinNetworkType networkType,
			ExtraNonceProvider extraNonceProvider, bool isPoS, double difficultyNormalizationFactor,
			IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
		{
			Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
			Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
			Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));
			Contract.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
			Contract.RequiresNonNull(headerHasher, nameof(headerHasher));
			Contract.RequiresNonNull(blockHasher, nameof(blockHasher));
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

			//blockTemplate = JsonConvert.DeserializeObject<GetBlockTemplateResult>("{\"capabilities\":[\"proposal\"],\"version\":536870912,\"rules\":[\"csv\",\"segwit\"],\"vbavailable\":{},\"vbrequired\":0,\"previousblockhash\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac4\",\"transactions\":[{\"data\":\"0100000001f209942b305d7e1ff4d4017f8e4a82946e35ab943d98049580a5ef446741c07f010000006a47304402201735be63bf6527f3a419eccd8dddd2a68bd652170a304b95a1f76e3a9a310908022044c86f2e3238aa8ec960206d5395fcbe69fa61a2671895fda2142474d01ecf280121039be344e1dc60c08355f4ba7cd3c7e0a57873aba19e782d281143a4ada0481663feffffff02002d3101000000001976a9147b108ab6bbffc56a6c1ce0d14edad1a588aac9f088ac5610ba17000000001976a9146f63066c9f43becbd89a2c5ef32268ef07b8907b88ac68ed0100\",\"txid\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"hash\":\"dd3a70fceb9e792fbd3f0cabdaba31edf46dc18937e0ff200e67a3531a6e5014\",\"depends\":[],\"fee\":22600,\"sigops\":8,\"weight\":900},{\"data\":\"01000000020749c01b88bbb998ea2bb829d2fd1509e064a865be25584ebb7d238c2d2c7ce6000000006b483045022100e1a30aec16addf31111acb977dfbbe8747a43b4fd810a6c53a4622be409e8921022024273c724935cf00bece0e07bdf48e2ca2144cd45805689b2ca801fbbe919bb20121031b392bcd589e62244a4d1529f1e21aba9defb0ffa71c5e2e14e22f3f5081e00afeffffffc6fba4e75b294a488c62b41bc02ebcfb9d33c1842960ef2c456676112cc134c7000000006b483045022100df744f016524b15a73d3f603613a4f8cb58bf082a10c1602d0a0f6f8c06de71a0220134babf4b34582d3fbcfbf0b70092a995c356c66053b767a6bfd684537ba66760121033a6da8e5dd47967df138f6d32d834571db1b90c5f4f531683078550aa98038dafeffffff02f3821100000000001976a914b52631c6125289fe5d59319c39c149aac2bc8b9b88ac40b9ec05000000001976a9145ea4e971f6007878ee2215ac3dbadae13b41585988ac68ed0100\",\"txid\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"hash\":\"7191c509b371ad24a43259cd3416e61b66647686f0a97c34fb289274ccc56132\",\"depends\":[],\"fee\":37400,\"sigops\":8,\"weight\":1496}],\"coinbaseaux\":{\"flags\":\"\"},\"coinbasevalue\":5000060000,\"longpollid\":\"33e9ec25751d0d5ad43660d925271923649e4dc675449a29342087ee26227ac455260\",\"target\":\"0000003713130000000000000000000000000000000000000000000000000000\",\"mintime\":1501243676,\"mutable\":[\"time\",\"transactions\",\"prevblock\"],\"noncerange\":\"00000000ffffffff\",\"sigoplimit\":80000,\"sizelimit\":4000000,\"weightlimit\":4000000,\"curtime\":1501245752,\"bits\":\"1d371313\",\"height\":126313,\"default_witness_commitment\":\"6a24aa21a9edc0669f40a7281cb4f8c88e35f084319af5408fe75fba1458c5ccf871a984c887\"}");

			this.poolConfig = poolConfig;
			this.clusterConfig = clusterConfig;
			this.poolAddressDestination = poolAddressDestination;
			this.networkType = networkType;
			this.blockTemplate = blockTemplate;
			this.jobId = jobId;

			extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
			this.isPoS = isPoS;
			this.difficultyNormalizationFactor = difficultyNormalizationFactor;

			this.coinbaseHasher = coinbaseHasher;
			this.headerHasher = headerHasher;
			this.blockHasher = blockHasher;
		}

		private readonly string jobId;
		private readonly BitcoinNetworkType networkType;
		private readonly int extraNoncePlaceHolderLength;
		private readonly GetBlockTemplateResult blockTemplate;
		private readonly ClusterConfig clusterConfig;
		private readonly PoolConfig poolConfig;
		private readonly IDestination poolAddressDestination;
		private readonly bool isPoS;
		private readonly double difficultyNormalizationFactor;
		private Target blockTarget;
		private MerkleTree mt;
		private Money rewardToPool;
		private byte[] coinbaseInitial;
		private byte[] coinbaseFinal;
		private readonly HashSet<string> submissions = new HashSet<string>();
		private readonly IHashAlgorithm coinbaseHasher;
		private readonly IHashAlgorithm headerHasher;
		private readonly IHashAlgorithm blockHasher;

		private static readonly Dictionary<CoinType, string> devFeeAddresses = new Dictionary<CoinType, string>
		{
			{CoinType.BTC, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm"},
			{CoinType.LTC, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC"},
		};

		// serialization constants
		private static byte[] scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes("/nodeStratum/"))).ToBytes();
		private static byte[] sha256Empty = Enumerable.Repeat((byte)0, 32).ToArray();
		private static uint txVersion = 1u; // transaction version (currently 1) - see https://en.bitcoin.it/wiki/Transaction
		private static uint txInputCount = 1u;
		private static uint txInPrevOutIndex = (uint)(Math.Pow(2, 32) - 1);
		private static uint txInSequence = 0;
		private static uint txLockTime = 0;

		///////////////////////////////////////////
		// GetJobParams related properties

		private string previousBlockHashReversedHex;
		private string[] merkleBranchesHex;
		private string coinbaseInitialHex;
		private string coinbaseFinalHex;
		private Transaction txOut;

		#region API-Surface

		public GetBlockTemplateResult BlockTemplate => blockTemplate;
		public string JobId => jobId;

		public void Init()
		{
			blockTarget = !string.IsNullOrEmpty(blockTemplate.Target) ?
				new Target(uint256.Parse(blockTemplate.Target)) :
				new Target(blockTemplate.Bits.HexToByteArray());

			previousBlockHashReversedHex = blockTemplate.PreviousBlockhash
				.HexToByteArray()
				.ReverseByteOrder()
				.ToHexString();

			BuildMerkleBranches();
			BuildCoinbase();
		}

		public object GetJobParams(bool isNew)
		{
			return new object[]
			{
				jobId,
				previousBlockHashReversedHex,
				coinbaseInitialHex,
				coinbaseFinalHex,
				merkleBranchesHex,
				blockTemplate.Version.ToString("x4"),
				blockTemplate.Bits,
				blockTemplate.CurTime.ToString("x4"),
				isNew
			};
		}

		public BitcoinShare ProcessShare(string extraNonce1, string extraNonce2, string nTime, string nonce, double stratumDifficulty)
		{
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce1), $"{nameof(extraNonce1)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

			// validate nTime
			if (nTime.Length != 8)
				throw new StratumException(StratumError.Other, "incorrect size of ntime");

			var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
			if (nTimeInt < blockTemplate.CurTime || nTimeInt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200)
				throw new StratumException(StratumError.Other, "ntime out of range");

			// validate nonce
			if (nonce.Length != 8)
				throw new StratumException(StratumError.Other, "incorrect size of nonce");

			var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

			// check for dupes
			if (!RegisterSubmit(extraNonce1, extraNonce2, nTime, nonce))
				throw new StratumException(StratumError.DuplicateShare, "duplicate share");

			return ProcessShareInternal(extraNonce1, extraNonce2, nTimeInt, nonceInt, stratumDifficulty);
		}

		#endregion // API-Surface

		private void BuildMerkleBranches()
		{
			var transactionHashes = blockTemplate.Transactions
				.Select(tx => (tx.TxId ?? tx.Hash)
					.HexToByteArray()
					.Reverse()
					.ToArray())
				.ToArray();

			mt = new MerkleTree(transactionHashes);

			merkleBranchesHex = mt.Steps
				.Select(x => x.ToHexString())
				.ToArray();
		}

		private void BuildCoinbase()
		{
			var extraNoncePlaceHolderLengthByte = (byte)extraNoncePlaceHolderLength;

			// generate script parts
			var sigScriptInitial = GenerateScriptSigInitial();
			var sigScriptInitialBytes = sigScriptInitial.ToBytes();

			var sigScriptLength = (uint)(
				sigScriptInitial.Length +
				extraNoncePlaceHolderLength +
				scriptSigFinalBytes.Length);

			// output transaction
			txOut = CreateOutputTransaction();

			// build coinbase initial
			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				// version
				bs.ReadWrite(ref txVersion);

				// timestamp for POS coins
				if (isPoS)
				{
					var timestamp = blockTemplate.CurTime;
					bs.ReadWrite(ref timestamp);
				}

				// serialize (simulated) input transaction
				bs.ReadWriteAsVarInt(ref txInputCount);
				bs.ReadWrite(ref sha256Empty);
				bs.ReadWrite(ref txInPrevOutIndex);

				// signature script initial part
				bs.ReadWriteAsVarInt(ref sigScriptLength);
				bs.ReadWrite(ref sigScriptInitialBytes);

				// emit a simulated OP_PUSH(n) just without the payload (which is filled in by the miner: extranonce1 and extranonce2)
				bs.ReadWrite(ref extraNoncePlaceHolderLengthByte);

				// done
				coinbaseInitial = stream.ToArray();
				coinbaseInitialHex = coinbaseInitial.ToHexString();
			}

			// build coinbase final
			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				// signature script final part
				bs.ReadWrite(ref scriptSigFinalBytes);

				// tx in sequence
				bs.ReadWrite(ref txInSequence);

				// serialize output transaction
				var txOutBytes = SerializeOutputTransaction(txOut);
				bs.ReadWrite(ref txOutBytes);

				// misc
				bs.ReadWrite(ref txLockTime);

				// done
				coinbaseFinal = stream.ToArray();
				coinbaseFinalHex = coinbaseFinal.ToHexString();
			}
		}

		private byte[] SerializeOutputTransaction(Transaction tx)
		{
			var withDefaultWitnessCommitment = !string.IsNullOrEmpty(blockTemplate.DefaultWitnessCommitment);

			var outputCount = (uint)tx.Outputs.Count;
			if (withDefaultWitnessCommitment)
				outputCount++;

			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				// write output count
				bs.ReadWriteAsVarInt(ref outputCount);

				long amount;
				byte[] raw;
				uint rawLength;

				// serialize witness
				if (withDefaultWitnessCommitment)
				{
					amount = 0;
					raw = blockTemplate.DefaultWitnessCommitment.HexToByteArray();
					rawLength = (uint)raw.Length;

					bs.ReadWrite(ref amount);
					bs.ReadWriteAsVarInt(ref rawLength);
					bs.ReadWrite(ref raw);
				}

				// serialize other recipients
				foreach (var output in tx.Outputs)
				{
					amount = output.Value.Satoshi;
					var outScript = output.ScriptPubKey;
					raw = outScript.ToBytes(true);
					rawLength = (uint)raw.Length;

					bs.ReadWrite(ref amount);
					bs.ReadWriteAsVarInt(ref rawLength);
					bs.ReadWrite(ref raw);
				}

				return stream.ToArray();
			}
		}

		private Script GenerateScriptSigInitial()
		{
			var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();    // 1501244088

			// script ops
			var ops = new List<Op>();

			// push block height
			ops.Add(Op.GetPushOp(blockTemplate.Height));

			// optionally push aux-flags
			if (!string.IsNullOrEmpty(blockTemplate.CoinbaseAux?.Flags))
				ops.Add(Op.GetPushOp(blockTemplate.CoinbaseAux.Flags.HexToByteArray()));

			// push timestamp
			ops.Add(Op.GetPushOp(now));

			return new Script(ops);
		}

		private Transaction CreateOutputTransaction()
		{
			var blockReward = new Money(blockTemplate.CoinbaseValue);
			var reward = blockReward;
			var tx = new Transaction();
			rewardToPool = blockReward;

			// Payee funds (DASH Coin only)
			if (!string.IsNullOrEmpty(blockTemplate.Payee))
			{
				var payeeAddress = BitcoinUtils.AddressToScript(blockTemplate.Payee);
				var payeeReward = reward / 5;

				reward -= payeeReward;
				rewardToPool -= payeeReward;

				tx.AddOutput(payeeReward, payeeAddress);
			}

			// Distribute funds to configured reward recipients
			var rewardRecipients = new List<RewardRecipient>(poolConfig.RewardRecipients);

			// Tiny donation to MiningForce developer(s)
			if (!clusterConfig.DisableDevDonation &&
			    networkType == BitcoinNetworkType.Main &&
			    devFeeAddresses.ContainsKey(poolConfig.Coin.Type))
				rewardRecipients.Add(new RewardRecipient
				{
					Address = devFeeAddresses[poolConfig.Coin.Type],
					Percentage = 0.2m
				});

			foreach (var recipient in rewardRecipients)
			{
				var recipientAddress = BitcoinUtils.AddressToScript(recipient.Address);
				var recipientReward = new Money((long)Math.Floor(recipient.Percentage / 100.0m * reward.Satoshi));

				rewardToPool -= recipientReward;

				tx.AddOutput(recipientReward, recipientAddress);
			}

			// Finally distribute remaining funds to pool
			tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddressDestination)
			{
				Value = rewardToPool
			});

			// validate it
			//var checkResult = tx.Check();
			//Debug.Assert(checkResult == TransactionCheckResult.Success);

			return tx;
		}

		private bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
		{
			var key = extraNonce1 + extraNonce2 + nTime + nonce;
			if (submissions.Contains(key))
				return false;

			submissions.Add(key);
			return true;
		}

		private BitcoinShare ProcessShareInternal(string extraNonce1, string extraNonce2, uint nTime, uint nonce, double stratumDifficulty)
		{
			// build coinbase
			var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
			var coinbaseHash = coinbaseHasher.Digest(coinbase, 0);

			// build merkle-root
			var merkleRoot = mt.WithFirst(coinbaseHash)
				.ToArray();

			// build block-header
			var blockHeader = new BlockHeader
			{
				Version = (int)blockTemplate.Version,
				Bits = new Target(Encoders.Hex.DecodeData(blockTemplate.Bits)),
				HashPrevBlock = uint256.Parse(blockTemplate.PreviousBlockhash),
				HashMerkleRoot = new uint256(merkleRoot),
				BlockTime = DateTimeOffset.FromUnixTimeSeconds(nTime),
				Nonce = nonce
			};

			// hash block-header
			var headerBytes = blockHeader.ToBytes();
			var headerHash = headerHasher.Digest(headerBytes, nTime);
			var headerValue = new uint256(headerHash, true);
			var target = new Target(headerValue);

			var shareDiffAdjusted = target.Difficulty * difficultyNormalizationFactor;
			var ratio = shareDiffAdjusted / stratumDifficulty;

			// test if share meets at least workers current difficulty
			if (ratio < 0.99)
				throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({target.Difficulty})");

			// valid share
			var result = new BitcoinShare
			{
				Difficulty = target.Difficulty,
				DifficultyNormalized = shareDiffAdjusted,
				BlockHeight = blockTemplate.Height
			};

			// now check if the share meets the much harder block difficulty (block candidate)
			if (target < blockTarget)
			{
				result.IsBlockCandidate = true;
				var blockBytes = SerializeBlock(headerBytes, coinbase);
				result.BlockHex = blockBytes.ToHexString();
				result.BlockHash = blockHasher.Digest(headerBytes, nTime).ToHexString();
				result.BlockHeight = blockTemplate.Height;
				result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
			}

			return result;
		}

		private byte[] SerializeCoinbase(string extraNonce1, string extraNonce2)
		{
			var extraNonce1Bytes = extraNonce1.HexToByteArray();
			var extraNonce2Bytes = extraNonce2.HexToByteArray();

			using (var stream = new MemoryStream())
			{
				stream.Write(coinbaseInitial);
				stream.Write(extraNonce1Bytes);
				stream.Write(extraNonce2Bytes);
				stream.Write(coinbaseFinal);

				return stream.ToArray();
			}
		}

		private byte[] SerializeBlock(byte[] header, byte[] coinbase)
		{
			var transactionCount = (uint)blockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
			var rawTransactionBuffer = BuildRawTransactionBuffer();

			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				bs.ReadWrite(ref header);
				bs.ReadWriteAsVarInt(ref transactionCount);
				bs.ReadWrite(ref coinbase);
				bs.ReadWrite(ref rawTransactionBuffer);

				// TODO: handle DASH coin masternode_payments

				// POS coins require a zero byte appended to block which the daemon replaces with the signature
				if (isPoS)
					bs.ReadWrite((byte)0);

				return stream.ToArray();
			}
		}

		private byte[] BuildRawTransactionBuffer()
		{
			using (var stream = new MemoryStream())
			{
				foreach (var tx in blockTemplate.Transactions)
				{
					var txRaw = tx.Data.HexToByteArray();
					stream.Write(txRaw);
				}

				return stream.ToArray();
			}
		}
	}
}
