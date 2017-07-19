using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MiningForce.Blockchain.Bitcoin.Commands;
using MiningForce.Configuration;
using MiningForce.Crypto;
using MiningForce.Extensions;
using NBitcoin;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinJob
    {
	    public BitcoinJob(PoolConfig poolConfig, ExtraNonceProvider extraNonceProvider, bool isPoS)
	    {
		    this.poolConfig = poolConfig;

			poolAddress = BitcoinAddress.Create(poolConfig.Address);
		    extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
		    this.isPoS = isPoS;
	    }

		private long jobId = 0;
	    private readonly int extraNoncePlaceHolderLength;
	    private GetBlockTemplateResponse blockTemplate;
	    private readonly BitcoinAddress poolAddress;
	    private readonly PoolConfig poolConfig;
	    private readonly bool isPoS;
	    private uint version;
	    private byte[] coinbaseInitial;
	    private byte[] coinbaseFinal;

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

		public bool ApplyTemplate(GetBlockTemplateResponse update, bool forceUpdate)
		{
			var isNew = blockTemplate == null || 
						blockTemplate.PreviousBlockhash != update.PreviousBlockhash ||
			            blockTemplate.Height < update.Height;

			if (!isNew && !forceUpdate)
				return false;

			jobId++;

			blockTemplate = update;
			version = blockTemplate.Version;
			timestamp = blockTemplate.CurTime;

			// fields for miner
			encodedDifficultyHex = blockTemplate.Bits;

            previousBlockHashReversedHex = blockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

			BuildMerkleBranches();
			BuildCoinbase();

			return isNew;
        }

	    private void BuildMerkleBranches()
	    {
		    var transactionHashes = blockTemplate.Transactions
			    .Select(tx => (tx.TxId ?? tx.Hash).HexToByteArray())
			    .Select(x =>
			    {
				    Array.Reverse(x);
				    return x;
			    })
			    .ToArray();

		    var mt = new MerkleTree(transactionHashes);

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
			    var payeeAddress = BitcoinAddress.Create(blockTemplate.Payee);
			    var payeeReward = reward / 5;

			    reward -= payeeReward;
			    rewardToPool -= payeeReward;

			    tx.AddOutput(payeeReward, payeeAddress);
		    }

		    // Distribute funds to configured reward recipients
		    foreach (var recipient in poolConfig.RewardRecipients)
		    {
			    var recipientAddress = BitcoinAddress.Create(recipient.Address);
			    var recipientReward = new Money((long) Math.Floor(recipient.Percentage / 100.0 * reward.Satoshi));

			    rewardToPool -= recipientReward;

			    tx.AddOutput(recipientReward, recipientAddress);
		    }

			// Finally distribute remaining funds to pool
		    tx.Outputs.Insert(0, new TxOut(rewardToPool, poolAddress)
		    {
			    Value = rewardToPool
			});

			// validate it
			//var checkResult = tx.Check();
			//Debug.Assert(checkResult == TransactionCheckResult.Success);

			return tx;
	    }

        public object GetJobParams(bool isNew)
        {
            return new object[]
            {
                jobId.ToString(CultureInfo.InvariantCulture),
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
	}
}
