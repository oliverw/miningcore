using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MiningForce.Blockchain.Bitcoin.DaemonResponses;
using MiningForce.Configuration;
using MiningForce.Crypto;
using MiningForce.Extensions;
using MiningForce.Stratum;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
	    public BitcoinJob(PoolConfig poolConfig, IDestination poolAddressDestination, BitcoinNetworkType networkType,
			ExtraNonceProvider extraNonceProvider, bool isPoS, double shareMultiplier, 
			IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher, BlockTemplate blockTemplate,
			string jobId)
	    {
		    this.poolConfig = poolConfig;
		    this.poolAddressDestination = poolAddressDestination;
			this.networkType = networkType;
			this.blockTemplate = blockTemplate;
		    this.jobId = jobId;

		    extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
		    this.isPoS = isPoS;
			this.shareMultiplier = shareMultiplier;

			this.coinbaseHasher = coinbaseHasher;
		    this.headerHasher = headerHasher;
		    this.blockHasher = blockHasher;
	    }

	    private readonly string jobId;
	    private readonly BitcoinNetworkType networkType;
	    private readonly int extraNoncePlaceHolderLength;
	    private readonly BlockTemplate blockTemplate;
	    private readonly PoolConfig poolConfig;
	    private readonly IDestination poolAddressDestination;
		private readonly bool isPoS;
	    private Target target;
	    private MerkleTree mt;
	    private uint version;
	    private byte[] coinbaseInitial;
	    private byte[] coinbaseFinal;
		private readonly HashSet<string> submissions = new HashSet<string>();
	    private readonly double shareMultiplier;
	    private readonly IHashAlgorithm coinbaseHasher;
	    private readonly IHashAlgorithm headerHasher;
	    private readonly IHashAlgorithm blockHasher;

	    private static readonly Dictionary<CoinType, string> devFeeAddresses = new Dictionary<CoinType, string>
	    {
		    {CoinType.Bitcoin, "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm"},
		    {CoinType.Litecoin, "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC"},
		};

		// serialization constants
		private static byte[] scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes("/MiningForce/"))).ToBytes();
	    private static byte[] sha256Empty = Enumerable.Repeat((byte)0, 32).ToArray();
	    private static uint txInputCount = 1u;
	    private static uint txInPrevOutIndex = (uint) (Math.Pow(2, 32) - 1);
	    private static uint txInSequence = 0;
	    private static uint txLockTime = 0;

		///////////////////////////////////////////
		// GetJobParams related properties

		private string previousBlockHashReversedHex;
	    private string[] merkleBranchesHex;
	    private string coinbaseInitialHex;
	    private string coinbaseFinalHex;
	    private uint timestamp;
	    private string encodedDifficultyHex;

	    #region API-Surface

	    public BlockTemplate BlockTemplate => blockTemplate;
	    public string JobId => jobId;

	    public void Init()
	    {
		    version = blockTemplate.Version;
		    timestamp = blockTemplate.CurTime;

		    target = !string.IsNullOrEmpty(blockTemplate.Target)
			    ? new Target(new uint256(blockTemplate.Target))
			    : new Target(blockTemplate.Bits.HexToByteArray());

		    // fields for miner
		    encodedDifficultyHex = blockTemplate.Bits;

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
			    version.ToString("x4"),
			    encodedDifficultyHex,
			    timestamp.ToString("x4"),
			    isNew
		    };
	    }

		public BitcoinShare ProcessShare(string extraNonce1, string extraNonce2, string nTime, string nonce)
	    {
		    // validate nTime
		    if (nTime?.Length != 8)
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

		    return ProcessShareInternal(extraNonce1, extraNonce2, nTimeInt, nonceInt);
	    }

		#endregion // API-Surface

		private void BuildMerkleBranches()
	    {
		    var transactionHashes = blockTemplate.Transactions
			    .Select(tx => (tx.TxId ?? tx.Hash).HexToByteArray())
				.ToReverseArray();

		    mt = new MerkleTree(transactionHashes);

			merkleBranchesHex = mt.Steps
				.Select(x => x.ToHexString())
				.ToArray();
	    }

	    private void BuildCoinbase()
	    {
		    var extraNoncePlaceHolderLengthByte = (byte) extraNoncePlaceHolderLength;

		    // generate script parts
		    var sigScriptInitial = GenerateScriptSigInitial();
		    var sigScriptInitialBytes = sigScriptInitial.ToBytes();

		    // output transaction
		    var txOut = CreateOutputTransaction();

		    // build coinbase initial
		    using (var stream = new MemoryStream())
		    {
			    var bs = new BitcoinStream(stream, true);

			    // version
			    bs.ReadWrite(ref version);

			    // timestamp for POS coins
			    if (isPoS)
				    bs.ReadWrite(ref timestamp);

			    // serialize (simulated) input transaction
			    bs.ReadWriteAsVarInt(ref txInputCount);
			    bs.ReadWrite(ref sha256Empty);
			    bs.ReadWrite(ref txInPrevOutIndex);

			    // signature script initial part
			    var sigScriptLength = (uint) (sigScriptInitial.Length + extraNoncePlaceHolderLength +
			                                  scriptSigFinalBytes.Length);
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

			var outputCount = (uint) tx.Outputs.Count;
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
				    rawLength = (uint) raw.Length;

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
				    rawLength = (uint) raw.Length;

				    bs.ReadWrite(ref amount);
				    bs.ReadWriteAsVarInt(ref rawLength);
				    bs.ReadWrite(ref raw);
			    }

			    return stream.ToArray();
		    }
	    }

		private Script GenerateScriptSigInitial()
	    {
			var ops = new List<Op>();

			// push block height
		    ops.Add(Op.GetPushOp(blockTemplate.Height));

			// optionally push aux-flags
			if(!string.IsNullOrEmpty(blockTemplate.CoinbaseAux?.Flags))
				ops.Add(Op.GetPushOp(blockTemplate.CoinbaseAux.Flags.HexToByteArray()));

		    // push timestamp
		    ops.Add(Op.GetPushOp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

			return new Script(ops);
	    }

	    private Transaction CreateOutputTransaction()
	    {
		    var blockReward = new Money(blockTemplate.CoinbaseValue);
		    var reward = blockReward;
		    var rewardToPool = blockReward;
		    var tx = new Transaction();

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

			// Developer fee
			if(networkType == BitcoinNetworkType.Main && devFeeAddresses.ContainsKey(poolConfig.Coin.Type))
				rewardRecipients.Add(new RewardRecipient { Address = devFeeAddresses[poolConfig.Coin.Type], Percentage = 0.1 });

			foreach (var recipient in rewardRecipients)
		    {
			    var recipientAddress = BitcoinUtils.AddressToScript(recipient.Address);
			    var recipientReward = new Money((long) Math.Floor(recipient.Percentage / 100.0 * reward.Satoshi));

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

	    private BitcoinShare ProcessShareInternal(string extraNonce1, string extraNonce2, uint nTime, uint nonce)
	    {
		    var coinbase = SerializeCoinbase(extraNonce1, extraNonce2);
		    var coinbaseHash = coinbaseHasher.Digest(coinbase, null);

		    var merkleRoot = mt.WithFirst(coinbaseHash)
				.ToReverseArray();

		    var header = SerializeHeader(merkleRoot, nTime, nonce);
		    var headerHash = headerHasher.Digest(header, nTime);
			var headerValue = new uint256(headerHash, true);
			var headerTarget = new Target(headerValue);

		    var result = new BitcoinShare
		    {
			    IsBlockCandidate = target >= headerTarget,
		    };

		    if (result.IsBlockCandidate)
			{
				result.BlockHex = SerializeBlock(header, coinbase).ToHexString();
				result.BlockHash = blockHasher.Digest(header, nTime).ToHexString();

				result.BlockHeight = blockTemplate.Height;
				result.BlockDiffAdjusted = target.Difficulty * shareMultiplier;
				result.Difficulty = Target.Difficulty1 / (headerValue.GetLow32() * shareMultiplier);
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

	    private byte[] SerializeHeader(byte[] merkleRoot, uint nTime, uint nonce)
	    {
		    using (var stream = new MemoryStream())
		    {
			    using (var writer = new BinaryWriter(stream))
			    {
				    writer.Write(nonce.ToBigEndian());
				    writer.Write(blockTemplate.Bits.HexToByteArray());
				    writer.Write(nTime.ToBigEndian());
				    writer.Write(merkleRoot);
				    writer.Write(blockTemplate.PreviousBlockhash.HexToByteArray());
					writer.Write(blockTemplate.Version.ToBigEndian());

					writer.Flush();

					return stream
						.ToArray()
						.ToReverseArray();
			    }
		    }
	    }

	    private byte[] SerializeBlock(byte[] header, byte[] coinbase)
	    {
		    var transactionCount = (uint) blockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
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
				    bs.ReadWrite((byte) 0);

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
