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
using System.Linq;
using System.Numerics;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Crypto;
using MiningCore.Extensions;
using MiningCore.Time;
using MiningCore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = NBitcoin.Transaction;

namespace MiningCore.Blockchain.BitcoinGold
{
    public class BitcoinGoldJob : ZCashJob
    {
        #region Overrides of ZCashJob

        protected override Transaction CreateOutputTransaction()
        {
            rewardToPool = new Money(BlockTemplate.CoinbaseValue * blockRewardMultiplier, MoneyUnit.Satoshi);

            var tx = new Transaction();

            // pool reward (t-addr)
            tx.AddOutput(rewardToPool, poolAddressDestination);

            return tx;
        }

        protected override byte[] SerializeHeader(uint nTime, string nonce)
        {
            // BTG requires the blockheight to be encoded in the first 4 bytes of the hashReserved field
            var heightAndReserved = BitConverter.GetBytes(BlockTemplate.Height)
                .Concat(Enumerable.Repeat((byte)0, 28))
                .ToArray();

            var blockHeader = new ZCashBlockHeader
            {
                Version = (int)BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                HashReserved = heightAndReserved,
                NTime = nTime,
                Nonce = nonce
            };

            return blockHeader.ToBytes();
        }

        public override void Init(ZCashBlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, BitcoinNetworkType networkType,
            bool isPoS, double shareMultiplier, decimal blockrewardMultiplier,
            IHashAlgorithm coinbaseHasher, IHashAlgorithm headerHasher, IHashAlgorithm blockHasher)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
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
            Difficulty = (double)new BigRational(coinbaseTxConfig.Diff1b, BlockTemplate.Target.HexToByteArray().ToBigInteger());

            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;

            if (!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseArray()
                .ToHexString();

            BuildCoinbase();

            // build tx hashes
            var txHashes = new List<uint256> { new uint256(coinbaseInitialHash) };
            txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.TxId.HexToByteArray().ReverseArray())));

            // build merkle root
            merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseArray();
            merkleRootReversed = merkleRoot.ReverseArray();
            merkleRootReversedHex = merkleRootReversed.ToHexString();

            jobParams = new object[]
            {
                JobId,
                BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
                previousBlockHashReversedHex,
                merkleRootReversedHex,
                BlockTemplate.Height.ReverseByteOrder().ToStringHex8() + sha256Empty.Take(28).ToHexString(), // height + hashReserved
                BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
                BlockTemplate.Bits.HexToByteArray().ReverseArray().ToHexString(),
                false
            };
        }

        #endregion
    }
}
