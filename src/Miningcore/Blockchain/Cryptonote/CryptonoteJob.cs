using Miningcore.Blockchain.Cryptonote.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Native;
using Miningcore.Stratum;
using Miningcore.Util;
using Org.BouncyCastle.Math;
using static Miningcore.Native.Cryptonight.Algorithm;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Cryptonote;

public class CryptonoteJob
{
    public CryptonoteJob(GetBlockTemplateResponse blockTemplate, byte[] instanceId, string jobId,
        CryptonoteCoinTemplate coin, PoolConfig poolConfig, ClusterConfig clusterConfig, string prevHash, string randomXRealm)
    {
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(clusterConfig);
        Contract.RequiresNonNull(instanceId);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        BlockTemplate = blockTemplate;
        PrepareBlobTemplate(instanceId);
        PrevHash = prevHash;
        RandomXRealm = randomXRealm;

        hashFunc = hashFuncs[coin.Hash];
    }

    protected delegate void HashFunc(string realm, string seedHex, ReadOnlySpan<byte> data, Span<byte> result, ulong height);

    protected static readonly Dictionary<CryptonightHashType, HashFunc> hashFuncs = new()
    {
        { CryptonightHashType.RandomX, (realm, seedHex, data, result, _) => RandomX.CalculateHash(realm, seedHex, data, result) },
        { CryptonightHashType.RandomARQ, (realm, seedHex, data, result, _) => RandomARQ.CalculateHash(realm, seedHex, data, result) },
        { CryptonightHashType.Crytonight0, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_0, height) },
        { CryptonightHashType.Crytonight1, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_1, height) },
        { CryptonightHashType.Crytonight2, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_2, height) },
        { CryptonightHashType.CrytonightHalf, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_HALF, height) },
        { CryptonightHashType.CrytonightDouble, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_DOUBLE, height) },
        { CryptonightHashType.CrytonightR, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_R, height) },
        { CryptonightHashType.CrytonightRTO, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_RTO, height) },
        { CryptonightHashType.CrytonightRWZ, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_RWZ, height) },
        { CryptonightHashType.CrytonightZLS, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_ZLS, height) },
        { CryptonightHashType.CrytonightCCX, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_CCX, height) },
        { CryptonightHashType.CrytonightGPU, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_GPU, height) },
        { CryptonightHashType.CrytonightFast, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_FAST, height) },
        { CryptonightHashType.CrytonightXAO, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_XAO, height) },
        { CryptonightHashType.Ghostrider, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, GHOSTRIDER_RTM, height) },
        { CryptonightHashType.CrytonightLite0, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_LITE_0, height) },
        { CryptonightHashType.CrytonightLite1, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_LITE_1, height) },
        { CryptonightHashType.CrytonightHeavy, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_HEAVY_0, height) },
        { CryptonightHashType.CrytonightHeavyXHV, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_HEAVY_XHV, height) },
        { CryptonightHashType.CrytonightHeavyTube, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_HEAVY_TUBE, height) },
        { CryptonightHashType.CrytonightPico, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, CN_PICO_0, height) },
        { CryptonightHashType.ArgonCHUKWA, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, AR2_CHUKWA, height) },
        { CryptonightHashType.ArgonCHUKWAV2, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, AR2_CHUKWA_V2, height) },
        { CryptonightHashType.ArgonWRKZ, (_, _, data, result, height) => Cryptonight.CryptonightHash(data, result, AR2_WRKZ, height) },
    };

    private byte[] blobTemplate;
    private int extraNonce;
    private readonly HashFunc hashFunc;

    private void PrepareBlobTemplate(byte[] instanceId)
    {
        blobTemplate = BlockTemplate.Blob.HexToByteArray();

        // inject instanceId
        instanceId.CopyTo(blobTemplate, BlockTemplate.ReservedOffset + CryptonoteConstants.ExtraNonceSize);
    }

    private string EncodeBlob(uint workerExtraNonce)
    {
        Span<byte> blob = stackalloc byte[blobTemplate.Length];
        blobTemplate.CopyTo(blob);

        // inject extranonce (big-endian) at the beginning of the reserved area
        var bytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
        bytes.CopyTo(blob[BlockTemplate.ReservedOffset..]);

        return CryptonoteBindings.ConvertBlob(blob, blobTemplate.Length).ToHexString();
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

        padded = padded[..size];
        padded.Reverse();

        return padded.ToHexString();
    }

    private void ComputeBlockHash(ReadOnlySpan<byte> blobConverted, Span<byte> result)
    {
        // blockhash is computed from the converted blob data prefixed with its length
        Span<byte> block = stackalloc byte[blobConverted.Length + 1];
        block[0] = (byte) blobConverted.Length;
        blobConverted.CopyTo(block[1..]);

        CryptonoteBindings.CryptonightHashFast(block, result);
    }

    #region API-Surface

    public string PrevHash { get; }
    public GetBlockTemplateResponse BlockTemplate { get; }
    public string RandomXRealm { get; set; }

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

    public (Share Share, string BlobHex) ProcessShare(string nonce, uint workerExtraNonce, string workerHash, StratumConnection worker)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(workerHash));
        Contract.Requires<ArgumentException>(workerExtraNonce != 0);

        var context = worker.ContextAs<CryptonoteWorkerContext>();

        // validate nonce
        if(!CryptonoteConstants.RegexValidNonce.IsMatch(nonce))
            throw new StratumException(StratumError.MinusOne, "malformed nonce");

        // clone template
        Span<byte> blob = stackalloc byte[blobTemplate.Length];
        blobTemplate.CopyTo(blob);

        // inject extranonce
        var bytes = BitConverter.GetBytes(workerExtraNonce.ToBigEndian());
        bytes.CopyTo(blob[BlockTemplate.ReservedOffset..]);

        // inject nonce
        bytes = nonce.HexToByteArray();
        bytes.CopyTo(blob[CryptonoteConstants.BlobNonceOffset..]);

        // convert
        var blobConverted = CryptonoteBindings.ConvertBlob(blob, blobTemplate.Length);
        if(blobConverted == null)
            throw new StratumException(StratumError.MinusOne, "malformed blob");

        // hash it
        Span<byte> headerHash = stackalloc byte[32];
        hashFunc(RandomXRealm, BlockTemplate.SeedHash, blobConverted, headerHash, BlockTemplate.Height);

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
