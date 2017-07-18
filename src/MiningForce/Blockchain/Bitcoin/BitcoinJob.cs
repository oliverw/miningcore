using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

		private long jobId = 1;
	    private readonly int extraNoncePlaceHolderLength;
	    private GetBlockTemplateResponse blockTemplate;
	    private readonly BitcoinAddress poolAddress;
	    private readonly PoolConfig poolConfig;
	    private readonly bool isPoS;

		// serialization constants
		private static readonly Script scriptSigFinal = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes("/MiningForce/")));
	    private static byte[] sha256Empty = Enumerable.Repeat((byte)0, 32).ToArray();
	    private static uint txInputCount = 1u;
	    private static uint txInPrevOutIndex = (uint) (Math.Pow(2, 32) - 1);
	    private static uint txInSequence = 0;
	    private static uint txLockTime = 0;

		///////////////////////////////////////////
		// GetJobParams related properties

		private string previousBlockHashReversed;
	    private string[] merkleBranches;
	    private string coinbaseInitial;
	    private string coinbaseFinal;
	    private uint version;
	    private uint timestamp;
	    private string encodedDifficulty;

	    public bool ApplyTemplate(GetBlockTemplateResponse update)
		{
			jobId++;

			var isNew = blockTemplate == null || 
						blockTemplate.PreviousBlockhash != update.PreviousBlockhash ||
			            blockTemplate.Height < update.Height;

			blockTemplate = update;

			// fields for miner
			encodedDifficulty = blockTemplate.Bits;
            timestamp = blockTemplate.CurTime;
            version = blockTemplate.Version;

            previousBlockHashReversed = blockTemplate.PreviousBlockhash
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
		    merkleBranches = mt.Steps.Select(x => x.ToHexString()).ToArray();
	    }

	    private void BuildCoinbase()
	    {
			// generate script parts
		    var scriptSigP1 = GenerateScriptSigInitial(extraNoncePlaceHolderLength);
		    var scriptSigP1Bytes = scriptSigP1.ToBytes();
			var scriptSigP2 = scriptSigFinal;
		    var scriptSigP2Bytes = scriptSigP2.ToBytes();

			// output transaction
			var txOut = CreateOutputTransaction();
		    var txOutBytes = txOut.ToBytes();

			// build coinbase initial
			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				// version
				bs.ReadWrite(ref version);

				// timestamp for POS coins
				if(isPoS)
					bs.ReadWrite(ref timestamp);

				// serialize (simulated) input tx
				bs.ReadWriteAsVarInt(ref txInputCount);
				bs.ReadWrite(ref sha256Empty);
				bs.ReadWriteAsVarInt(ref txInPrevOutIndex);

				// signature script part 1
				var sigScriptLength = (uint) (scriptSigP1.Length + extraNoncePlaceHolderLength + scriptSigP2.Length);
				bs.ReadWriteAsVarInt(ref sigScriptLength);
				bs.ReadWrite(ref scriptSigP1Bytes);

				// done
				coinbaseInitial = stream.ToArray().ToHexString();
			}

		    // build coinbase final
		    using (var stream = new MemoryStream())
		    {
			    var bs = new BitcoinStream(stream, true);

			    // signature script part 2
			    bs.ReadWrite(ref scriptSigP2Bytes);

				// serialize (simulated) input tx
			    bs.ReadWrite(ref txOutBytes);

			    // misc
			    bs.ReadWrite(ref txLockTime);

				// done
				coinbaseFinal = stream.ToArray().ToHexString();
		    }
		}

		private Script GenerateScriptSigInitial(int extraNoncePlaceholderLength)
	    {
		    var placeholder = Enumerable.Repeat((byte) 0, extraNoncePlaceholderLength).ToArray();
			var ops = new List<Op>();

			// push block height
		    ops.Add(Op.GetPushOp(blockTemplate.Height));

			// optionally push aux-flags
			if(!string.IsNullOrEmpty(blockTemplate.CoinbaseAux?.Flags))
				ops.Add(Op.GetPushOp(blockTemplate.CoinbaseAux.Flags.HexToByteArray()));

		    // push timestamp
		    ops.Add(Op.GetPushOp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

			// push placeholder
		    ops.Add(Op.GetPushOp(placeholder));

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
			    var recipientReward = new Money((long)((recipient.Percentage / 100.0) * reward.Satoshi));

			    reward -= recipientReward;
			    rewardToPool -= recipientReward;

			    tx.AddOutput(recipientReward, recipientAddress);
		    }

		    // Finally distribute remaining funds to pool
		    tx.AddOutput(rewardToPool, poolAddress);

		    // validate it
		    //var checkResult = tx.Check();
		    //Debug.Assert(checkResult == TransactionCheckResult.Success);

		    return tx;
	    }

		private void BuildBlockFromTemplate()
	    {
			var target = string.IsNullOrEmpty(blockTemplate.Target)
				? new Target(uint.Parse(blockTemplate.Bits, NumberStyles.HexNumber))
				: new Target(uint256.Parse(blockTemplate.Target));

			Block block = new Block();
		    block.Header.Bits = target;
		    block.Header.BlockTime = Utils.UnixTimeToDateTime(blockTemplate.CurTime);
		    block.Header.Version = (int)blockTemplate.Version;
		    block.Header.HashPrevBlock = uint256.Parse(blockTemplate.PreviousBlockhash);
		}

        public object GetJobParams()
        {
            return new object[]
            {
                jobId.ToString("x"),
                previousBlockHashReversed,
                coinbaseInitial,
                coinbaseFinal,
                merkleBranches,
                version,
                encodedDifficulty,
                timestamp,
            };
        }
	}
}
