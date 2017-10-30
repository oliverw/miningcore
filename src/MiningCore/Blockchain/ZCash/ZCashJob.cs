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
using System.Collections.Generic;
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
using MiningCore.Extensions;
using MiningCore.Time;
using NBitcoin;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashJob : BitcoinJob<ZCashBlockTemplate>
    {
	    private ZCashCoinbaseTxConfig coinbaseTxConfig;
        private decimal blockReward;
	    private decimal rewardFees;

	    private uint coinbaseIndex = 4294967295u;
		private uint coinbaseSequence = 4294967295u;
		private readonly IHashAlgorithm sha256D = new Sha256D();
	    private byte[] coinbaseInitialHash;
	    private byte[] merkleRoot;
	    private byte[] merkleRootReversed;

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

		protected override byte[] SerializeHeader(byte[] coinbaseHash, uint nTime, uint nonce)
        {
            return base.SerializeHeader(coinbaseHash, nTime, nonce);
        }

        #endregion

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
		}
	}
}
