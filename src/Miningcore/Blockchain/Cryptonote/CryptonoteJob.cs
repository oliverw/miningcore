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
using System.Linq;
using System.Threading;
using Miningcore.Blockchain.Cryptonote.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Util;
using NBitcoin.BouncyCastle.Math;
using static Miningcore.Native.LibCryptonight;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Cryptonote
{
    public class CryptonoteJob
    {
        public CryptonoteJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(instanceId, nameof(instanceId));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            coin = poolConfig.Template.As<CryptonoteCoinTemplate>();
            BlockTemplate = blockTemplate;
            PrepareBlobTemplate(instanceId);

            switch (coin.Hash)
            {
                case CryptonightHashType.Normal:
                    hashFunc = LibCryptonight.Cryptonight;
                    break;

                case CryptonightHashType.Lite:
                    hashFunc = LibCryptonight.CryptonightLight;
                    break;

                case CryptonightHashType.Heavy:
                    hashFunc = LibCryptonight.CryptonightHeavy;
                    break;
            }
        }

        private byte[] blobTemplate;
        private int extraNonce;
        private readonly CryptonoteCoinTemplate coin;
        private readonly LibCryptonight.CryptonightHash hashFunc;

        private void PrepareBlobTemplate(byte[] instanceId)
        {
            blobTemplate = BlockTemplate.Blob.HexToByteArray();

            // inject instanceId at the end of the reserved area of the blob
            var destOffset = (int) BlockTemplate.ReservedOffset + CryptonoteConstants.ExtraNonceSize;
            Array.Copy(instanceId, 0, blobTemplate, destOffset, 3);
        }

        private string EncodeBlob(uint workerExtraNonce)
        {
            Span<byte> blob = stackalloc byte[blobTemplate.Length];
            blobTemplate.CopyTo(blob);

            // inject extranonce (big-endian at the beginning of the reserved area of the blob)
            var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
            extraNonceBytes.CopyTo(blob.Slice(BlockTemplate.ReservedOffset, extraNonceBytes.Length));

            return LibCryptonote.ConvertBlob(blob, blobTemplate.Length).ToHexString();
        }

        private string EncodeTarget(double difficulty)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = CryptonoteConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray().AsSpan();
            Span<byte> padded = stackalloc byte[32];

            var padLength = padded.Length - bytes.Length;

            if (padLength > 0)
                bytes.CopyTo(padded.Slice(padLength, bytes.Length));

            padded = padded.Slice(0, 4);
            padded.Reverse();

            return padded.ToHexString();
        }

        private void ComputeBlockHash(ReadOnlySpan<byte> blobConverted, Span<byte> result)
        {
            // blockhash is computed from the converted blob data prefixed with its length
            Span<byte> block = stackalloc byte[blobConverted.Length + 1];
            block[0] = (byte) blobConverted.Length;
            blobConverted.CopyTo(block.Slice(1));

            LibCryptonote.CryptonightHashFast(block, result);
        }

        #region API-Surface

        public GetBlockTemplateResponse BlockTemplate { get; }

        public void PrepareWorkerJob(CryptonoteWorkerJob workerJob, out string blob, out string target)
        {
            workerJob.Height = BlockTemplate.Height;
            workerJob.ExtraNonce = (uint) Interlocked.Increment(ref extraNonce);

            if (extraNonce < 0)
                extraNonce = 0;

            blob = EncodeBlob(workerJob.ExtraNonce);
            target = EncodeTarget(workerJob.Difficulty);
        }

        public (Share Share, string BlobHex) ProcessShare(string nonce, uint workerExtraNonce, string workerHash, StratumClient worker)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workerHash), $"{nameof(workerHash)} must not be empty");
            Contract.Requires<ArgumentException>(workerExtraNonce != 0, $"{nameof(workerExtraNonce)} must not be empty");

            var context = worker.ContextAs<CryptonoteWorkerContext>();

            // validate nonce
            if (!CryptonoteConstants.RegexValidNonce.IsMatch(nonce))
                throw new StratumException(StratumError.MinusOne, "malformed nonce");

            // clone template
            Span<byte> blob = stackalloc byte[blobTemplate.Length];
            blobTemplate.CopyTo(blob);

            // inject extranonce
            var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
            extraNonceBytes.CopyTo(blob.Slice(BlockTemplate.ReservedOffset, extraNonceBytes.Length));

            // inject nonce
            var nonceBytes = nonce.HexToByteArray();
            nonceBytes.CopyTo(blob.Slice(CryptonoteConstants.BlobNonceOffset, nonceBytes.Length));

            // convert
            var blobConverted = LibCryptonote.ConvertBlob(blob, blobTemplate.Length);
            if (blobConverted == null)
                throw new StratumException(StratumError.MinusOne, "malformed blob");

            // determine variant
            CryptonightVariant variant;

            if (coin.HashVariant != 0)
                variant = (CryptonightVariant)coin.HashVariant;
            else
            {
                switch(blobConverted[0])
                {
                    case 9:
                    case 8:
                        variant = CryptonightVariant.VARIANT_2;
                        break;

                    case 7:
                        variant = CryptonightVariant.VARIANT_1;
                        break;

                    default:
                        variant = CryptonightVariant.VARIANT_0;
                        break;
                }
            }

            // hash it
            Span<byte> headerHash = stackalloc byte[32];
            hashFunc(blobConverted, headerHash, variant);

            var headerHashString = headerHash.ToHexString();
            if (headerHashString != workerHash)
                throw new StratumException(StratumError.MinusOne, "bad hash");

            // check difficulty
            var headerValue = headerHash.ToBigInteger();
            var shareDiff = (double) new BigRational(CryptonoteConstants.Diff1b, headerValue);
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;
            var isBlockCandidate = shareDiff >= BlockTemplate.Difficulty;

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
                Difficulty = stratumDifficulty,
            };

            if(isBlockCandidate)
            {
                // Compute block hash
                Span<byte> blockHash = stackalloc byte[32];
                ComputeBlockHash(blobConverted, blockHash);

                // Fill in block-relevant fields
                result.IsBlockCandidate = true;
                result.BlockHash = blockHash.ToHexString();
            }

            return (result, blob.ToHexString());
        }

        #endregion // API-Surface
    }
}
