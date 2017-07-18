using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
	    public BitcoinJob(PoolConfig poolConfig, ExtraNonceProvider extraNonceProvider)
	    {
		    this.poolConfig = poolConfig;

			poolAddress = BitcoinAddress.Create(poolConfig.Address);
		    extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
	    }

		private long jobId = 1;
	    private readonly int extraNoncePlaceHolderLength;
	    private GetBlockTemplateResponse blockTemplate;
	    private readonly BitcoinAddress poolAddress;
	    private readonly PoolConfig poolConfig;

		///////////////////////////////////////////
		// GetJobParams related properties

		private string previousBlockHashReversed;
	    private string[] merkleBranches;
	    private string coinbaseInitial;
	    private string coinbaseFinal;
	    private string version;
	    private string encodedDifficulty;
	    private string timestamp;

	    public bool ApplyTemplate(GetBlockTemplateResponse update)
		{
			jobId++;

			var isNew = blockTemplate == null || 
						blockTemplate.PreviousBlockhash != update.PreviousBlockhash ||
			            blockTemplate.Height < update.Height;

			blockTemplate = update;

			// fields for miner
			encodedDifficulty = blockTemplate.Bits;
            timestamp = BitConverter.GetBytes(blockTemplate.CurTime.ToBigEndian()).ToHexString();
            version = BitConverter.GetBytes(blockTemplate.Version.ToBigEndian()).ToHexString();

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
		    var scriptSigP2 = GenerateScriptSigFinal("/MiningForce/");

			// output transaction
		    var tx = CreateOutputTransaction();
		    var txHex = tx.ToHex();
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

	    private Script GenerateScriptSigFinal(string signature)
	    {
		    return new Script(Op.GetPushOp(Encoding.UTF8.GetBytes(signature)));
	    }

	    private Transaction CreateOutputTransaction()
	    {
		    var blockReward = new Money(blockTemplate.CoinbaseValue);
		    var reward = blockReward;
		    var rewardToPool = blockReward;

		    // build tx
		    var tx = new Transaction();

		    // Payee output (DASH Coin only)
		    if (!string.IsNullOrEmpty(blockTemplate.Payee))
		    {
			    var payeeAddress = BitcoinAddress.Create(blockTemplate.Payee);
			    var payeeReward = reward / 5;

			    reward -= payeeReward;
			    rewardToPool -= payeeReward;

			    tx.AddOutput(payeeReward, payeeAddress);
		    }

		    // Reward Recipient Outputs
		    foreach (var recipient in poolConfig.RewardRecipients)
		    {
			    var recipientAddress = BitcoinAddress.Create(recipient.Address);
			    var recipientReward = new Money((long)((recipient.Percentage / 100.0) * reward.Satoshi));

			    reward -= recipientReward;
			    rewardToPool -= recipientReward;

			    tx.AddOutput(recipientReward, recipientAddress);
		    }

		    // Pool Output
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
