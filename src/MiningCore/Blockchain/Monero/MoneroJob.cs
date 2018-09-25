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
using MiningCore.Blockchain.Monero.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Native;
using MiningCore.Stratum;
using MiningCore.Util;
using NBitcoin.BouncyCastle.Math;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Blockchain.Monero
{
    public class MoneroJob
    {
        public MoneroJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId,
            PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(instanceId, nameof(instanceId));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            coin = poolConfig.Coin.Type;
            BlockTemplate = blockTemplate;
            PrepareBlobTemplate(instanceId);
        }

        private byte[] blobTemplate;
        private uint extraNonce;
        private readonly CoinType coin;

        private void PrepareBlobTemplate(byte[] instanceId)
        {
            blobTemplate = BlockTemplate.Blob.HexToByteArray();

            // inject instanceId at the end of the reserved area of the blob
            var destOffset = (int) BlockTemplate.ReservedOffset + MoneroConstants.ExtraNonceSize;
            Array.Copy(instanceId, 0, blobTemplate, destOffset, 3);
        }

        private unsafe string EncodeBlob(uint workerExtraNonce)
        {
            Span<byte> blob = stackalloc byte[blobTemplate.Length];
            blobTemplate.CopyTo(blob);

            // inject extranonce (big-endian at the beginning of the reserved area of the blob)
            var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
            extraNonceBytes.CopyTo(blob.Slice(BlockTemplate.ReservedOffset, extraNonceBytes.Length));

            var result = LibCryptonote.ConvertBlob(blob, blobTemplate.Length).ToHexString();
            return result;
        }

        private string EncodeTarget(double difficulty)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = MoneroConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray().AsSpan();
            Span<byte> padded = stackalloc byte[32];

            var padLength = padded.Length - bytes.Length;

            if (padLength > 0)
                bytes.CopyTo(padded.Slice(padLength, bytes.Length));

            padded = padded.Slice(0, 4);
            padded.Reverse();

            var result = padded.ToHexString();
            return result;
        }

        private void ComputeBlockHash(byte[] blobConverted, Span<byte> result)
        {
            // blockhash is computed from the converted blob data prefixed with its length
            var bytes = new[] { (byte) blobConverted.Length }
                .Concat(blobConverted)
                .ToArray();

            LibCryptonote.CryptonightHashFast(bytes, result);
        }

        #region API-Surface

        public GetBlockTemplateResponse BlockTemplate { get; }

        public void Init()
        {
        }

        public void PrepareWorkerJob(MoneroWorkerJob workerJob, out string blob, out string target)
        {
            workerJob.Height = BlockTemplate.Height;
            workerJob.ExtraNonce = ++extraNonce;

            blob = EncodeBlob(workerJob.ExtraNonce);
            target = EncodeTarget(workerJob.Difficulty);
        }

        public unsafe (Share Share, string BlobHex, string BlobHash) ProcessShare(string nonce, uint workerExtraNonce, string workerHash, StratumClient worker)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workerHash), $"{nameof(workerHash)} must not be empty");
            Contract.Requires<ArgumentException>(workerExtraNonce != 0, $"{nameof(workerExtraNonce)} must not be empty");

            var context = worker.ContextAs<MoneroWorkerContext>();

            // validate nonce
            if (!MoneroConstants.RegexValidNonce.IsMatch(nonce))
                throw new StratumException(StratumError.MinusOne, "malformed nonce");

            // clone template
            Span<byte> blob = stackalloc byte[blobTemplate.Length];
            blobTemplate.CopyTo(blob);

            // inject extranonce
            var extraNonceBytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
            extraNonceBytes.CopyTo(blob.Slice(BlockTemplate.ReservedOffset, extraNonceBytes.Length));

            // inject nonce
            var nonceBytes = nonce.HexToByteArray();
            nonceBytes.CopyTo(blob.Slice(MoneroConstants.BlobNonceOffset, nonceBytes.Length));

            // convert
            var blobConverted = LibCryptonote.ConvertBlob(blob, blobTemplate.Length);
            if (blobConverted == null)
                throw new StratumException(StratumError.MinusOne, "malformed blob");

            // hash it
            Span<byte> headerHash = stackalloc byte[32];

            switch (coin)
            {
                case CoinType.AEON:
                    LibCryptonight.CryptonightLight(blobConverted, headerHash, 0);
                    break;

                case CoinType.XMR:
                    var variant = blobConverted[0] >= 7 ? blobConverted[0] - 6 : 0;
                    LibCryptonight.Cryptonight(blobConverted, headerHash, variant);
                    break;

                default:
                    LibCryptonight.Cryptonight(blobConverted, headerHash, 0);
                    break;
            }

            var headerHashString = headerHash.ToHexString();
            if (headerHashString != workerHash)
                throw new StratumException(StratumError.MinusOne, "bad hash");

            // check difficulty
            var headerValue = headerHash.ToBigInteger();
            var shareDiff = (double) new BigRational(MoneroConstants.Diff1b, headerValue);
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

            // Compute block hash
            Span<byte> blockHash = stackalloc byte[32];
            ComputeBlockHash(blobConverted, blockHash);

            var result = new Share
            {
                BlockHeight = BlockTemplate.Height,
                IsBlockCandidate = isBlockCandidate,
                BlockHash = blockHash.ToHexString(),
                Difficulty = stratumDifficulty,
            };

            var blobHex = blob.ToHexString();
            var blobHash = blockHash.ToHexString();

            return (result, blobHex, blobHash);
        }

        #endregion // API-Surface
    }
}
