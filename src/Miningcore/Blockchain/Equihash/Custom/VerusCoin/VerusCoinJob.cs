using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Equihash.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.Extensions;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using Transaction = NBitcoin.Transaction;
using Miningcore.Stratum;
using NBitcoin.Zcash;

namespace Miningcore.Blockchain.Equihash.Custom.VerusCoin
{
    public class VerusCoinJob : EquihashJob
    {

        protected override  Transaction CreateOutputTransaction()
        {
            var txNetwork = Network.GetNetwork(networkParams.CoinbaseTxNetwork);
            var tx1 = Transaction.Create(txNetwork);
            var tx = new ZcashTransaction(tx1.ToHex());

            // set versions
            tx.Version = txVersion;

            overwinterField.SetValue(tx, true);
            versionGroupField.SetValue(tx, txVersionGroupId);

            // pool reward (t-addr)
            rewardToPool = new Money(blockReward + rewardFees, MoneyUnit.Satoshi);
            tx.Outputs.Add(rewardToPool, poolAddressDestination);
            

            return tx;
        }

        protected override byte[] SerializeHeader(uint nTime, string nonce)
        {
            var blockHeader = new EquihashBlockHeader
            {
                Version = (int) BlockTemplate.Version,
                Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
                HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
                HashMerkleRoot = new uint256(merkleRoot),
                NTime = nTime,
                Nonce = nonce
            };

            if(isSaplingActive && !string.IsNullOrEmpty(BlockTemplate.FinalSaplingRootHash))
                blockHeader.HashReserved = BlockTemplate.FinalSaplingRootHash.HexToReverseByteArray();
            
            if(!string.IsNullOrEmpty(BlockTemplate.Solution))
                blockHeader.SolutionIn = BlockTemplate.Solution.HexToReverseByteArray().ToHexString();

            return blockHeader.ToBytes();
        }


    

            protected override (Share Share, string BlockHex) ProcessShareInternal(StratumClient worker, string nonce,
            uint nTime, string solution)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var solutionBytes = (Span<byte>) solution.HexToByteArray();

            // serialize block-header
            var headerBytes = SerializeHeader(nTime, nonce);

            // concat header and solution
			var length =  headerBytes.Length+3 ;
          
            Span<byte> headerSolutionBytes = stackalloc byte[length];
            headerBytes.CopyTo(headerSolutionBytes);
            
            solutionBytes.CopyTo(headerSolutionBytes.Slice(140));

            // hash block-header
            Span<byte> headerHash = stackalloc byte[32];
     
            headerHasherverus.Digest(headerSolutionBytes, headerHash, headerSolutionBytes.Length);
            var headerValue = new uint256(headerHash);

            // calc share-diff
            var shareDiff = (double) new BigRational(networkParams.Diff1BValue, headerHash.ToBigInteger());
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;

            // check if the share meets the much harder block difficulty (block candidate)
            var isBlockCandidate = headerValue <= blockTargetValue;

            // test if share meets at least workers current difficulty
            if(!isBlockCandidate && ratio < 0.99)
            {
                // check if share matched the previous difficulty from before a vardiff retarget
                if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / context.PreviousDifficulty.Value;

                    if(ratio < 0.99)
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

            if(isBlockCandidate)
            {
                var headerHashReversed = headerHash.ToNewReverseArray();

                result.IsBlockCandidate = true;
                result.BlockReward = rewardToPool.ToDecimal(MoneyUnit.BTC);
                result.BlockHash = headerHashReversed.ToHexString();
                var headertemp = headerBytes.Take(140).ToArray();
                var blockBytes = SerializeBlock(headertemp, coinbaseInitial, solutionBytes);
                var blockHex = blockBytes.ToHexString();
            
                return (result, blockHex);
            }

            return (result, null);
        }

        public override void Init(EquihashBlockTemplate blockTemplate, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig, IMasterClock clock,
            IDestination poolAddressDestination, Network network,
            EquihashSolver solver)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(poolAddressDestination, nameof(poolAddressDestination));
            Contract.RequiresNonNull(solver, nameof(solver));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            this.clock = clock;
            this.poolAddressDestination = poolAddressDestination;
            coin = poolConfig.Template.As<EquihashCoinTemplate>();
            networkParams = coin.GetNetwork(network.NetworkType);
            this.network = network;
            BlockTemplate = blockTemplate;
            JobId = jobId;
            Difficulty = (double) new BigRational(networkParams.Diff1BValue, BlockTemplate.Target.HexToReverseByteArray().AsSpan().ToBigInteger());

            // ZCash Sapling & Overwinter support
            isSaplingActive = true;

            isOverwinterActive = true;
          
            txVersion = 4;
            txVersionGroupId = 2301567109;
            

            // Misc
            this.solver = solver;

            if(!string.IsNullOrEmpty(BlockTemplate.Target))
                blockTargetValue = new uint256(BlockTemplate.Target);
            else
            {
                var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
                blockTargetValue = tmp.ToUInt256();
            }

            previousBlockHashReversedHex = BlockTemplate.PreviousBlockhash
                .HexToByteArray()
                .ReverseInPlace()
                .ToHexString();

            if(blockTemplate.Subsidy != null)
                blockReward = blockTemplate.Subsidy.Miner * BitcoinConstants.SatoshisPerBitcoin;
            else
                blockReward = BlockTemplate.CoinbaseValue;

            if(networkParams?.PayFoundersReward == true)
            {
                var founders = blockTemplate.Subsidy.Founders ?? blockTemplate.Subsidy.Community;

                if(!founders.HasValue)
                    throw new Exception("Error, founders reward missing for block template");

                blockReward = (blockTemplate.Subsidy.Miner + founders.Value) * BitcoinConstants.SatoshisPerBitcoin;
            }

            rewardFees = blockTemplate.Transactions.Sum(x => x.Fee);

            BuildCoinbase();

            // build tx hashes
            var txHashes = new List<uint256> { new uint256(coinbaseInitialHash) };
            txHashes.AddRange(BlockTemplate.Transactions.Select(tx => new uint256(tx.Hash.HexToReverseByteArray())));

            // build merkle root
            merkleRoot = MerkleNode.GetRoot(txHashes).Hash.ToBytes().ReverseInPlace();
            merkleRootReversed = merkleRoot.ReverseInPlace();
            merkleRootReversedHex = merkleRootReversed.ToHexString();

            // misc
            var hashReserved = isSaplingActive && !string.IsNullOrEmpty(blockTemplate.FinalSaplingRootHash) ?
                blockTemplate.FinalSaplingRootHash.HexToReverseByteArray().ToHexString() :
                sha256Empty.ToHexString();
            char[] charsToTrim = {'0'};
            var solutionIn = !string.IsNullOrEmpty(blockTemplate.Solution) ?
                blockTemplate.Solution.HexToByteArray().ToHexString().TrimEnd(charsToTrim) :
                null;

            jobParams = new object[]
            {
                JobId,
                BlockTemplate.Version.ReverseByteOrder().ToStringHex8(),
                previousBlockHashReversedHex,
                merkleRootReversedHex,
                hashReserved,
                BlockTemplate.CurTime.ReverseByteOrder().ToStringHex8(),
                BlockTemplate.Bits.HexToReverseByteArray().ToHexString(),
                false,
                solutionIn
            };
        }
    }
}
