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
                blockHeader.SolutionIn = BlockTemplate.Solution.HexToReverseByteArray();

            return blockHeader.ToBytes();
        }


    }
}