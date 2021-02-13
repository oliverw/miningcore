
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
        public CryptonoteJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId, PoolConfig poolConfig, ClusterConfig clusterConfig, string prevHash)
        {
            Contract.RequiresNonNull(blockTemplate, nameof(blockTemplate));
            Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(instanceId, nameof(instanceId));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId), $"{nameof(jobId)} must not be empty");

            coin = poolConfig.Template.As<CryptonoteCoinTemplate>();
            BlockTemplate = blockTemplate;
            PrepareBlobTemplate(instanceId);
            PrevHash = prevHash;

            // add for RandomX
            if(!string.IsNullOrEmpty(blockTemplate.SeedHash))
                seedHashBytes = blockTemplate.SeedHash.HexToByteArray();

            switch(coin.Hash)
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

                case CryptonightHashType.RandomX:
                    hashFunc = LibCryptonight.RandomX;
                    break;
            }
        }

        private byte[] blobTemplate;
        private readonly byte[] seedHashBytes;   // add for RandomX
        private int extraNonce;
        private readonly CryptonoteCoinTemplate coin;
        private readonly LibCryptonight.CryptonightHash hashFunc;

        private void PrepareBlobTemplate(byte[] instanceId)
        {
            blobTemplate = BlockTemplate.Blob.HexToByteArray();

            // inject instanceId at the end of the reserved area of the blob
            var destOffset = BlockTemplate.ReservedOffset + CryptonoteConstants.ExtraNonceSize;
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

        private string EncodeTarget(double difficulty, int size = 4)
        {
            var diff = BigInteger.ValueOf((long) (difficulty * 255d));
            var quotient = CryptonoteConstants.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray().AsSpan();
            Span<byte> padded = stackalloc byte[32];

            var padLength = padded.Length - bytes.Length;

            if(padLength > 0)
                bytes.CopyTo(padded.Slice(padLength, bytes.Length));

            padded = padded.Slice(0, size);
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

        public string PrevHash { get; }
        public GetBlockTemplateResponse BlockTemplate { get; }

        public void PrepareWorkerJob(CryptonoteWorkerJob workerJob, out string blob, out string target)
        {
            workerJob.Height = BlockTemplate.Height;
            workerJob.ExtraNonce = (uint) Interlocked.Increment(ref extraNonce);
            workerJob.SeedHash = BlockTemplate.SeedHash;

            if(extraNonce < 0)
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
            if(!CryptonoteConstants.RegexValidNonce.IsMatch(nonce))
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
            if(blobConverted == null)
                throw new StratumException(StratumError.MinusOne, "malformed blob");

            Console.WriteLine($"Coin: {coin.Name} Symbol: {coin.Symbol} Family: {coin.Family} Hash: {coin.Hash} Variant: {coin.HashVariant}");
            Console.WriteLine("------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"blob Converted: {blobConverted[0]}");

            //  -------- NEED TO CHANGE ---------->>

            // determine variant
            CryptonightVariant variant = CryptonightVariant.VARIANT_0;

            if(coin.HashVariant != 0)
                variant = (CryptonightVariant) coin.HashVariant;
            else
            {
                switch(coin.Hash)
                {
                    case CryptonightHashType.Normal:
                        variant = (blobConverted[0] >= 10) ? CryptonightVariant.VARIANT_4 :
                                 ((blobConverted[0] >= 8) ? CryptonightVariant.VARIANT_2 :
                                 ((blobConverted[0] == 7) ? CryptonightVariant.VARIANT_1 :
                            CryptonightVariant.VARIANT_0));
                        break;

                    case CryptonightHashType.Lite:
                        variant = CryptonightVariant.VARIANT_1;
                        break;

                    case CryptonightHashType.Heavy:
                        variant = CryptonightVariant.VARIANT_0;
                        break;

                    case CryptonightHashType.RandomX:
                        variant = CryptonightVariant.VARIANT_0;
                        break;

                    default:
                        break;
                }
            }
            // <<-------------

            Console.WriteLine($"");

            // hash it
            Span<byte> headerHash = stackalloc byte[32];
            hashFunc(blobConverted, BlockTemplate.SeedHash, headerHash, variant, BlockTemplate.Height);
            
            var headerHashString = headerHash.ToHexString();
            if(headerHashString != workerHash)
                throw new StratumException(StratumError.MinusOne, "bad hash");

            // check difficulty
            var headerValue = headerHash.ToBigInteger();
            var shareDiff = (double) new BigRational(CryptonoteConstants.Diff1b, headerValue);
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;
            var isBlockCandidate = shareDiff >= BlockTemplate.Difficulty;

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
