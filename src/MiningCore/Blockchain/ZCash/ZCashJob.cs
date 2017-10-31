/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Crypto;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Equihash;
using MiningCore.Extensions;
using MiningCore.Stratum;
using MiningCore.Time;
using MiningCore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashJob : BitcoinJob<ZCashBlockTemplate>
    {
	    protected ZCashCoinbaseTxConfig coinbaseTxConfig;
	    protected decimal blockReward;
	    protected decimal rewardFees;

	    protected uint coinbaseIndex = 4294967295u;
	    protected uint coinbaseSequence = 4294967295u;
	    protected readonly IHashAlgorithm sha256D = new Sha256D();
	    protected byte[] coinbaseInitialHash;
	    protected byte[] merkleRoot;
	    protected byte[] merkleRootReversed;
	    protected string merkleRootReversedHex;
		protected static EquihashVerifier equihash = new EquihashVerifier(4);

		#region Overrides of BitcoinJob<ZCashBlockTemplate>

		protected override Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue, MoneyUnit.Satoshi);

            var tx = new Transaction();

	        if (coinbaseTxConfig.PayFoundersReward &&
	            (coinbaseTxConfig.MaxFoundersRewardBlockHeight >= BlockTemplate.Height ||
	             coinbaseTxConfig.TreasuryRewardStartBlockHeight > 0))
	        {
		        // founders or treasury reward?
		        if (coinbaseTxConfig.TreasuryRewardStartBlockHeight > 0 && 
					BlockTemplate.Height >= coinbaseTxConfig.TreasuryRewardStartBlockHeight)
		        {
					// pool reward (t-addr)
					var amount = new Money(Math.Round(blockReward * (1m - (coinbaseTxConfig.PercentTreasuryReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
			        tx.AddOutput(amount, poolAddressDestination);

					// treasury reward (t-addr)
			        var index = (int)Math.Floor((BlockTemplate.Height - coinbaseTxConfig.TreasuryRewardStartBlockHeight) /
						coinbaseTxConfig.TreasuryRewardAddressChangeInterval % coinbaseTxConfig.TreasuryRewardAddresses.Length);

			        var destination = BitcoinUtils.AddressToScript(coinbaseTxConfig.TreasuryRewardAddresses[index]);
					amount = new Money(Math.Round(blockReward * (coinbaseTxConfig.PercentTreasuryReward / 100m)), MoneyUnit.Satoshi);
			        tx.AddOutput(amount, destination);
				}

		        else
		        {
			        // pool reward (t-addr)
			        var amount = new Money(Math.Round(blockReward * (1m - (coinbaseTxConfig.PercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
			        tx.AddOutput(amount, poolAddressDestination);

			        // founders reward (t-addr)
			        var index = (int)Math.Floor(BlockTemplate.Height / coinbaseTxConfig.FoundersRewardAddressChangeInterval);

			        var destination = BitcoinUtils.AddressToScript(coinbaseTxConfig.FoundersRewardAddresses[index]);
			        amount = new Money(Math.Round(blockReward * (coinbaseTxConfig.PercentFoundersReward / 100m)), MoneyUnit.Satoshi);
			        tx.AddOutput(amount, destination);
		        }
			}

	        else
	        {
				// no founders reward
				// pool reward (t-addr)
		        var amount = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
		        tx.AddOutput(amount, poolAddressDestination);
	        }

			return tx;
        }

		protected override void BuildCoinbase()
        {
	        var script = TxIn.CreateCoinbase((int)BlockTemplate.Height).ScriptSig;

			// output transaction
			txOut = CreateOutputTransaction();

			using (var stream = new MemoryStream())
			{
				var bs = new BitcoinStream(stream, true);

				// version
				bs.ReadWrite(ref txVersion);

				// serialize (simulated) input transaction
				bs.ReadWriteAsVarInt(ref txInputCount);
				bs.ReadWrite(ref sha256Empty);
				bs.ReadWrite(ref coinbaseIndex);
				bs.ReadWrite(ref script);
				bs.ReadWrite(ref coinbaseSequence);

				// serialize output transaction
				var txOutBytes = SerializeOutputTransaction(txOut);
				bs.ReadWrite(ref txOutBytes);

				// misc
				bs.ReadWrite(ref txLockTime);

				// done
				coinbaseInitial = stream.ToArray();
				coinbaseInitialHex = coinbaseInitial.ToHexString();
				coinbaseInitialHash = sha256D.Digest(coinbaseInitial);
			}
		}

	    public override object GetJobParams(bool isNew)
	    {
		    return new object[]
		    {
			    JobId,
			    BlockTemplate.Version.ToStringHex8(),
				previousBlockHashReversedHex,
			    merkleRootReversedHex,
				sha256Empty,	// hashReserved
			    BlockTemplate.CurTime.ToStringHex8(),
			    BlockTemplate.Bits.HexToByteArray().ToReverseArray().ToHexString(),
			    isNew
		    };
	    }

		public override void Init(ZCashBlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock,
			IDestination poolAddressDestination, BitcoinNetworkType networkType,
            BitcoinExtraNonceProvider extraNonceProvider, bool isPoS, double shareMultiplier,
            IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
	        Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.RequiresNonNull(extraNonceProvider, nameof(extraNonceProvider));
            Contract.RequiresNonNull(coinbaseHasher, nameof(coinbaseHasher));
            Contract.RequiresNonNull(headerHasher, nameof(headerHasher));
            Contract.RequiresNonNull(blockHasher, nameof(blockHasher));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.poolConfig = poolConfig;
            this.clusterConfig = clusterConfig;
	        this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            this.networkType = networkType;

            if (ZCashConstants.CoinbaseTxConfig.TryGetValue(poolConfig.Coin.Type, out var coinbaseTx))
                coinbaseTx.TryGetValue(networkType, out coinbaseTxConfig);

            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = new Target(new NBitcoin.BouncyCastle.Math.BigInteger(BlockTemplate.Target, 16)).Difficulty;

            extraNoncePlaceHolderLength = extraNonceProvider.PlaceHolder.Length;
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            this.coinbaseHasher = coinbaseHasher;
            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;

            blockTargetValue = BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber);

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseByteOrder()
                .ToHexString();

            blockReward = blockTemplate.Subsidy.Miner * BitcoinConstants.SatoshisPerBitcoin;

            if (coinbaseTxConfig?.PayFoundersReward == true)
            {
                var founders = blockTemplate.Subsidy.Founders ?? blockTemplate.Subsidy.Community;

                if (!founders.HasValue)
                    throw new Exception("Error, founders reward missing for block template");

                blockReward = (blockTemplate.Subsidy.Miner + founders.Value) * BitcoinConstants.SatoshisPerBitcoin;
            }

            rewardFees = blockTemplate.Transactions.Sum(x => x.Fee);

            BuildCoinbase();
            BuildMerkleBranches();

	        merkleRoot = mt.WithFirst(coinbaseInitialHash);
	        merkleRootReversed = merkleRoot.ToReverseArray();
	        merkleRootReversedHex = merkleRootReversed.ToHexString();
        }

		#endregion

		public BitcoinShare ProcessShare(StratumClient<BitcoinWorkerContext> worker,
			string extraNonce2, string nTime, string nonce, string solution)
		{
			Contract.RequiresNonNull(worker, nameof(worker));
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");
			Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(solution), $"{nameof(solution)} must not be empty");

			// validate nTime
			if (nTime.Length != 8)
				throw new StratumException(StratumError.Other, "incorrect size of ntime");

			var nTimeInt = uint.Parse(nTime, NumberStyles.HexNumber);
			if (nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset)clock.UtcNow).ToUnixTimeSeconds() + 7200)
				throw new StratumException(StratumError.Other, "ntime out of range");

			// validate nonce
			if (nonce.Length != 64)
				throw new StratumException(StratumError.Other, "incorrect size of nonce");

			// validate solution
			if (solution.Length != 2694)
				throw new StratumException(StratumError.Other, "incorrect size of solution");

			var nonceInt = uint.Parse(nonce, NumberStyles.HexNumber);

			// dupe check
			if (!RegisterSubmit(worker.Context.ExtraNonce1, extraNonce2, nTime, nonce))
				throw new StratumException(StratumError.DuplicateShare, "duplicate share");

			return ProcessShareInternal(worker, extraNonce2, nTimeInt, nonceInt, solution);
		}

		protected virtual byte[] SerializeHeader(uint nTime, uint nonce)
	    {
		    var blockHeader = new ZCashBlockHeader
		    {
			    Version = (int)BlockTemplate.Version,
			    Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
			    HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
			    HashMerkleRoot = new uint256(merkleRoot),
			    HashReserved = new uint256(),
			    NTime = nTime,
			    Nonce = nonce
		    };

		    return blockHeader.ToBytes();
	    }

		protected byte[] SerializeBlock(byte[] header, byte[] coinbase, byte[] solution)
	    {
		    var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx
		    var rawTransactionBuffer = BuildRawTransactionBuffer();

		    using (var stream = new MemoryStream())
		    {
			    var bs = new BitcoinStream(stream, true);

			    bs.ReadWrite(ref header);
			    bs.ReadWrite(ref solution);
				bs.ReadWriteAsVarInt(ref transactionCount);
			    bs.ReadWrite(ref coinbase);
			    bs.ReadWrite(ref rawTransactionBuffer);

			    return stream.ToArray();
		    }
	    }

		protected virtual BitcoinShare ProcessShareInternal(StratumClient<BitcoinWorkerContext> worker, string extraNonce2, 
			uint nTime, uint nonce, string solution)
		{
			var solutionBytes = solution.HexToByteArray();

			// hash block-header
			var headerBytes = SerializeHeader(nTime, nonce); // 144 bytes (doesn't contain soln)
			var headerSolutionBytes = headerBytes.Concat(solutionBytes).ToArray();
			var headerHash = headerHasher.Digest(headerSolutionBytes, (ulong)nTime);
			var headerValue = BigInteger.Parse("0" + headerHash.ToReverseArray().ToHexString(), NumberStyles.HexNumber);

			// verify solution
			if(!equihash.Verify(headerBytes, solutionBytes.Skip(3).ToArray()))	// skip preamble (3 bytes)
				throw new StratumException(StratumError.Other, "invalid solution");

			// calc share-diff
			var shareDiff = (double)new BigRational(BitcoinConstants.Diff1, headerValue) * shareMultiplier;
			var stratumDifficulty = worker.Context.Difficulty;
			var ratio = shareDiff / stratumDifficulty;

			// check if the share meets the much harder block difficulty (block candidate)
			var isBlockCandidate = headerValue < blockTargetValue;

			// test if share meets at least workers current difficulty
			if (!isBlockCandidate && ratio < 0.99)
			{
				// check if share matched the previous difficulty from before a vardiff retarget
				if (worker.Context.VarDiff?.LastUpdate != null && worker.Context.PreviousDifficulty.HasValue)
				{
					ratio = shareDiff / worker.Context.PreviousDifficulty.Value;

					if (ratio < 0.99)
						throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

					// use previous difficulty
					stratumDifficulty = worker.Context.PreviousDifficulty.Value;
				}

				else
					throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
			}

			var result = new BitcoinShare
			{
				BlockHeight = BlockTemplate.Height,
				IsBlockCandidate = isBlockCandidate
			};

			var blockBytes = SerializeBlock(headerBytes, coinbaseInitial, solutionBytes);
			result.BlockHex = blockBytes.ToHexString();
			result.BlockHash = headerHash.ToReverseArray().ToHexString();
			result.BlockHeight = BlockTemplate.Height;
			result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
			result.Difficulty = stratumDifficulty;

			return result;
		}

	}
}
