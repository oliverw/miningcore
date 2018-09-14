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
using System.Reflection;
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
using NBitcoin.Zcash;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashJob : BitcoinJob<ZCashBlockTemplate>
    {
        protected ZCashChainConfig chainConfig;
        protected decimal blockReward;
        protected decimal rewardFees;

        protected uint coinbaseIndex = 4294967295u;
        protected uint coinbaseSequence = 4294967295u;
        protected uint txVersionGroupId;
        protected uint txExpiryHeight = 20;
        protected uint txNJoinSplits = 0;
        protected readonly IHashAlgorithm sha256D = new Sha256D();
        protected byte[] coinbaseInitialHash;
        protected byte[] merkleRoot;
        protected byte[] merkleRootReversed;
        protected string merkleRootReversedHex;
        protected EquihashSolverBase equihash;

        // ZCash Sapling & Overwinter support
        protected bool isOverwinterActive = false;
        protected bool isSaplingActive = false;

        // temporary reflection hack to force overwinter
        protected static FieldInfo overwinterField = typeof(ZcashTransaction).GetField("fOverwintered", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        protected static FieldInfo versionGroupField = typeof(ZcashTransaction).GetField("nVersionGroupId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        #region Overrides of BitcoinJob<ZCashBlockTemplate>

        protected override Transaction CreateOutputTransaction()
        {
            var tx = chainConfig.CreateCoinbaseTx();

            if (isOverwinterActive)
                overwinterField.SetValue(tx, true);

            // set versions
            tx.Version = txVersion;
            versionGroupField.SetValue(tx, txVersionGroupId);

            // calculate outputs
            if (chainConfig.PayFoundersReward &&
                (chainConfig.LastFoundersRewardBlockHeight >= BlockTemplate.Height ||
                 chainConfig.TreasuryRewardStartBlockHeight > 0))
            {
                // founders or treasury reward?
                if (chainConfig.TreasuryRewardStartBlockHeight > 0 &&
                    BlockTemplate.Height >= chainConfig.TreasuryRewardStartBlockHeight)
                {
                    // pool reward (t-addr)
                    rewardToPool = new Money(Math.Round(blockReward * (1m - (chainConfig.PercentTreasuryReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                    tx.AddOutput(rewardToPool, poolAddressDestination);

                    // treasury reward (t-addr)
                    var destination = FoundersAddressToScriptDestination(GetTreasuryRewardAddress());
                    var amount = new Money(Math.Round(blockReward * (chainConfig.PercentTreasuryReward / 100m)), MoneyUnit.Satoshi);
                    tx.AddOutput(amount, destination);
                }

                else
                {
                    // pool reward (t-addr)
                    rewardToPool = new Money(Math.Round(blockReward * (1m - (chainConfig.PercentFoundersReward) / 100m)) + rewardFees, MoneyUnit.Satoshi);
                    tx.AddOutput(rewardToPool, poolAddressDestination);

                    // founders reward (t-addr)
                    var destination = FoundersAddressToScriptDestination(GetFoundersRewardAddress());
                    var amount = new Money(Math.Round(blockReward * (chainConfig.PercentFoundersReward / 100m)), MoneyUnit.Satoshi);
                    tx.AddOutput(amount, destination);
                }
            }

            else
            {
                // no founders reward
                // pool reward (t-addr)
                rewardToPool = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
                tx.AddOutput(rewardToPool, poolAddressDestination);
            }

            return tx;
        }

        protected override void BuildCoinbase()
        {
            // output transaction
            txOut = CreateOutputTransaction();
            txOut.AddInput(TxIn.CreateCoinbase((int) BlockTemplate.Height));

            using (var stream = new MemoryStream())
            {
                var bs = new ZcashStream(stream, true);
                bs.ReadWrite(ref txOut);

                // done
                coinbaseInitial = stream.ToArray();
                coinbaseInitialHex = coinbaseInitial.ToHexString();
                coinbaseInitialHash = sha256D.Digest(coinbaseInitial);
            }
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

            if (ZCashConstants.Chains.TryGetValue(poolConfig.Coin.Type, out var chain))
                chain.TryGetValue(networkType, out chainConfig);

            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = (double) new BigRational(chainConfig.Diff1b, BlockTemplate.Target.HexToByteArray().ReverseArray().ToBigInteger());
            txExpiryHeight = blockTemplate.Height + 100;

            // ZCash Sapling & Overwinter support
            isSaplingActive = chainConfig.SaplingActivationHeight.HasValue &&
                              chainConfig.SaplingActivationHeight.Value > 0 &&
                              blockTemplate.Height >= chainConfig.SaplingActivationHeight.Value;

            isOverwinterActive = isSaplingActive ||
                                 chainConfig.OverwinterActivationHeight.HasValue &&
                                 chainConfig.OverwinterActivationHeight.Value > 0 &&
                                 blockTemplate.Height >= chainConfig.OverwinterActivationHeight.Value;

            if (isSaplingActive)
            {
                txVersion = 4;
                txVersionGroupId = 0x892F2085;
            }

            else if (isOverwinterActive)
            {
                txVersion = 3;
                txVersionGroupId = 0x03C48270;
            }

            // Misc
            this.isPoS = isPoS;
            this.shareMultiplier = shareMultiplier;

            this.headerHasher = headerHasher;
            this.blockHasher = blockHasher;
            this.equihash = chainConfig.Solver();

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

            blockReward = blockTemplate.Subsidy.Miner * BitcoinConstants.SatoshisPerBitcoin;

            if (chainConfig?.PayFoundersReward == true)
            {
                var founders = blockTemplate.Subsidy.Founders ?? blockTemplate.Subsidy.Community;

                if (!founders.HasValue)
                    throw new Exception("Error, founders reward missing for block template");

                blockReward = (blockTemplate.Subsidy.Miner + founders.Value) * BitcoinConstants.SatoshisPerBitcoin;
            }

            rewardFees = blockTemplate.Transactions.Sum(x => x.Fee);

            BuildCoinbase();

            // build tx hashes
            var txHashes = new List<uint256> {new uint256(coinbaseInitialHash)};
            txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.Hash.HexToByteArray().ReverseArray())));

            // build merkle root
            merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseArray();
            merkleRootReversed = merkleRoot.ReverseArray();
            merkleRootReversedHex = merkleRootReversed.ToHexString();

            // misc
            var hashReserved = isSaplingActive && !string.IsNullOrEmpty(blockTemplate.FinalSaplingRootHash) ? blockTemplate.FinalSaplingRootHash.HexToByteArray().ReverseArray().ToHexString() : sha256Empty.ToHexString();

            jobParams = new object[]
            {
                JobId,
                BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
                previousBlockHashReversedHex,
                merkleRootReversedHex,
                hashReserved,
                BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
                BlockTemplate.Bits.HexToByteArray().ReverseArray().ToHexString(),
                false
            };
        }

        #endregion

        public override (Share Share, string BlockHex) ProcessShare(StratumClient worker, string extraNonce2, string nTime, string solution)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(solution), $"{nameof(solution)} must not be empty");

            var context = worker.ContextAs<BitcoinWorkerContext>();

            // validate nTime
            if (nTime.Length != 8)
                throw new StratumException(StratumError.Other, "incorrect size of ntime");

            var nTimeInt = uint.Parse(nTime.HexToByteArray().ReverseArray().ToHexString(), NumberStyles.HexNumber);
            if (nTimeInt < BlockTemplate.CurTime || nTimeInt > ((DateTimeOffset) clock.Now).ToUnixTimeSeconds() + 7200)
                throw new StratumException(StratumError.Other, "ntime out of range");

            var nonce = context.ExtraNonce1 + extraNonce2;

            // validate nonce
            if (nonce.Length != 64)
                throw new StratumException(StratumError.Other, "incorrect size of extraNonce2");

            // validate solution
            if (solution.Length != (chainConfig.SolutionSize + chainConfig.SolutionPreambleSize) * 2)
                throw new StratumException(StratumError.Other, "incorrect size of solution");

            // dupe check
            if (!RegisterSubmit(nonce, solution))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, nonce, nTimeInt, solution);
        }

        protected virtual byte[] SerializeHeader(uint nTime, string nonce)
        {
            var blockHeader = new ZCashBlockHeader
            {
                Version = (int) BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                NTime = nTime,
                Nonce = nonce
            };

            if (isSaplingActive && !string.IsNullOrEmpty(BlockTemplate.FinalSaplingRootHash))
                blockHeader.HashReserved = BlockTemplate.FinalSaplingRootHash.HexToByteArray().ReverseArray();

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

        protected virtual (Share Share, string BlockHex) ProcessShareInternal(StratumClient worker, string nonce,
            uint nTime, string solution)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var solutionBytes = solution.HexToByteArray();

            // serialize block-header
            var headerBytes = SerializeHeader(nTime, nonce); // 144 bytes (doesn't contain soln)

            // verify solution
            if (!equihash.Verify(headerBytes, solutionBytes.Skip(chainConfig.SolutionPreambleSize).ToArray())) // skip preamble (3 bytes)
                throw new StratumException(StratumError.Other, "invalid solution");

            // hash block-header
            var headerSolutionBytes = headerBytes.Concat(solutionBytes).ToArray();
            var headerHash = headerHasher.Digest(headerSolutionBytes, (ulong) nTime);
            var headerHashReversed = headerHash.ToReverseArray();
            var headerValue = new uint256(headerHash);

            // calc share-diff
            var shareDiff = (double) new BigRational(chainConfig.Diff1b, headerHash.ToBigInteger()) * shareMultiplier;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

            // check if the share meets the much harder block difficulty (block candidate)
            var isBlockCandidate = headerValue <= blockTargetValue;

            // test if share meets at least workers current difficulty
            if (!isBlockCandidate && ratio < 0.99)
            {
                // check if share matched the previous difficulty from before a vardiff retarget
                if (context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                    // use previous difficulty
                    stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty,
            };

            if (isBlockCandidate)
            {
                result.IsBlockCandidate = true;
                result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
                result.BlockHash = headerHashReversed.ToHexString();

                var blockBytes = SerializeBlock(headerBytes, coinbaseInitial, solutionBytes);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }

        protected bool RegisterSubmit(string nonce, string solution)
        {
            lock (submissions)
            {
                var key = nonce.ToLower() + solution.ToLower();
                if (submissions.Contains(key))
                    return false;

                submissions.Add(key);
                return true;
            }
        }

        public string GetFoundersRewardAddress()
        {
            var maxHeight = chainConfig.LastFoundersRewardBlockHeight;

            var addressChangeInterval = (maxHeight + (ulong) chainConfig.FoundersRewardAddresses.Length) / (ulong) chainConfig.FoundersRewardAddresses.Length;
            var index = BlockTemplate.Height / addressChangeInterval;

            var address = chainConfig.FoundersRewardAddresses[index];
            return address;
        }

        protected string GetTreasuryRewardAddress()
        {
            var index = (int) Math.Floor((BlockTemplate.Height - chainConfig.TreasuryRewardStartBlockHeight) /
                                         chainConfig.TreasuryRewardAddressChangeInterval % chainConfig.TreasuryRewardAddresses.Length);

            var address = chainConfig.TreasuryRewardAddresses[index];
            return address;
        }

        public static IDestination FoundersAddressToScriptDestination(string address)
        {
            var decoded = Encoders.Base58.DecodeData(address);
            var hash = decoded.Skip(2).Take(20).ToArray();
            var result = new ScriptId(hash);
            return result;
        }
    }
}
