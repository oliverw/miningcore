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

namespace Miningcore.Blockchain.Equihash.Custom.VerusCoin
{
    public class VerusCoinJob : EquihashJob
    {
        protected override (Share Share, string BlockHex) ProcessShareInternal(StratumClient worker, string nonce,
            uint nTime, string solution)
        {
            var context = worker.ContextAs<BitcoinWorkerContext>();
            var solutionBytes = (Span<byte>) solution.HexToByteArray();

            // serialize block-header
            var headerBytes = SerializeHeader(nTime, nonce);

            // verify solution
            //  if(!solver.Verify(headerBytes, solutionBytes.Slice(networkParams.SolutionPreambleSize)))
            //     throw new StratumException(StratumError.Other, "invalid solution");

            // concat header and solution
            Span<byte> headerSolutionBytes = stackalloc byte[headerBytes.Length + solutionBytes.Length];
            headerBytes.CopyTo(headerSolutionBytes);
            solutionBytes.CopyTo(headerSolutionBytes.Slice(headerBytes.Length));

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

                var blockBytes = SerializeBlock(headerBytes, coinbaseInitial, solutionBytes);
                var blockHex = blockBytes.ToHexString();

                return (result, blockHex);
            }

            return (result, null);
        }
    }
}