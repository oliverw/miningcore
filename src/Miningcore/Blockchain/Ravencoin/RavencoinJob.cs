using System.Globalization;
using System.Text;
using Miningcore.Blockchain.Bitcoin;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Kawpow;
using Miningcore.Extensions;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using NBitcoin;
using NBitcoin.DataEncoders;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Ravencoin;

public class RavencoinJobParams
{
    public ulong Height { get; init; }
    public bool CleanJobs { get; set; }
}

public class RavencoinJob : BitcoinJob
{
    private Cache kawpowHasher;
    private new RavencoinJobParams jobParams;

    private byte[] SerializeHeader(Span<byte> coinbaseHash)
    {
        // build merkle-root
        var merkleRoot = mt.WithFirst(coinbaseHash.ToArray());

        // Build version
        var version = BlockTemplate.Version;

#pragma warning disable 618
        var blockHeader = new BlockHeader
#pragma warning restore 618
        {
            Version = unchecked((int) version),
            Bits = new Target(Encoders.Hex.DecodeData(BlockTemplate.Bits)),
            HashPrevBlock = uint256.Parse(BlockTemplate.PreviousBlockhash),
            HashMerkleRoot = new uint256(merkleRoot),
            BlockTime = DateTimeOffset.FromUnixTimeSeconds(BlockTemplate.CurTime),
            Nonce = BlockTemplate.Height
        };

        return blockHeader.ToBytes();
    }

    public (Share Share, string BlockHex) ProcessShareInternal(ILogger logger,
        StratumConnection worker, ulong nonce, string inputHeaderHash, string mixHash)
    {
        var context = worker.ContextAs<RavencoinWorkerContext>();
        var extraNonce1 = context.ExtraNonce1;

        // build coinbase
        var coinbase = SerializeCoinbase(extraNonce1);
        Span<byte> coinbaseHash = stackalloc byte[32];
        coinbaseHasher.Digest(coinbase, coinbaseHash);

        // hash block-header
        var headerBytes = SerializeHeader(coinbaseHash);
        Span<byte> headerHash = stackalloc byte[32];
        headerHasher.Digest(headerBytes, headerHash);
        headerHash.Reverse();

        var headerHashHex = headerHash.ToHexString();

        if(headerHashHex != inputHeaderHash)
            throw new StratumException(StratumError.MinusOne, $"bad header-hash");

        if(!kawpowHasher.Compute(logger, (int) BlockTemplate.Height, headerHash.ToArray(), nonce, out var mixHashOut, out var resultBytes))
            throw new StratumException(StratumError.MinusOne, "bad hash");

        if(mixHash != mixHashOut.ToHexString())
            throw new StratumException(StratumError.MinusOne, $"bad mix-hash");

        resultBytes.ReverseInPlace();
        mixHashOut.ReverseInPlace();

        var resultValue = new uint256(resultBytes);
        var resultValueBig = resultBytes.AsSpan().ToBigInteger();
        // calc share-diff
        var shareDiff = (double) new BigRational(RavencoinConstants.Diff1, resultValueBig) * shareMultiplier;
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;

        // check if the share meets the much harder block difficulty (block candidate)
        var isBlockCandidate = resultValue <= blockTargetValue;

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
            Difficulty = stratumDifficulty / shareMultiplier,
        };

        if(!isBlockCandidate)
        {
            return (result, null);
        }

        result.IsBlockCandidate = true;
        result.BlockHash = resultBytes.ReverseInPlace().ToHexString();

        var blockBytes = SerializeBlock(headerBytes, coinbase, nonce, mixHashOut);
        var blockHex = blockBytes.ToHexString();

        return (result, blockHex);
    }

    private byte[] SerializeCoinbase(string extraNonce1)
    {
        var extraNonce1Bytes = extraNonce1.HexToByteArray();

        using var stream = new MemoryStream();
        {
            stream.Write(coinbaseInitial);
            stream.Write(extraNonce1Bytes);
            stream.Write(coinbaseFinal);

            return stream.ToArray();
        }
    }

    private byte[] SerializeBlock(byte[] header, byte[] coinbase, ulong nonce, byte[] mixHash)
    {
        var rawTransactionBuffer = BuildRawTransactionBuffer();
        var transactionCount = (uint) BlockTemplate.Transactions.Length + 1; // +1 for prepended coinbase tx

        using var stream = new MemoryStream();
        {
            var bs = new BitcoinStream(stream, true);

            bs.ReadWrite(ref header);
            bs.ReadWrite(ref nonce);
            bs.ReadWrite(ref mixHash);
            bs.ReadWriteAsVarInt(ref transactionCount);

            bs.ReadWrite(ref coinbase);
            bs.ReadWrite(ref rawTransactionBuffer);

            return stream.ToArray();
        }
    }

    #region API-Surface

    public void Init(BlockTemplate blockTemplate, string jobId,
        PoolConfig pc, BitcoinPoolConfigExtra extraPoolConfig,
        ClusterConfig cc, IMasterClock clock,
        IDestination poolAddressDestination, Network network,
        bool isPoS, double shareMultiplier, IHashAlgorithm coinbaseHasher,
        IHashAlgorithm headerHasher, Cache kawpowHasher)
    {
        Contract.RequiresNonNull(blockTemplate);
        Contract.RequiresNonNull(pc);
        Contract.RequiresNonNull(cc);
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(poolAddressDestination);
        Contract.RequiresNonNull(coinbaseHasher);
        Contract.RequiresNonNull(headerHasher);
        Contract.RequiresNonNull(kawpowHasher);
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(jobId));

        this.coin = pc.Template.As<RavencoinTemplate>();
        this.txVersion = coin.CoinbaseTxVersion;
        this.network = network;
        this.clock = clock;
        this.poolAddressDestination = poolAddressDestination;
        this.BlockTemplate = blockTemplate;
        this.JobId = jobId;

        var coinbaseString = !string.IsNullOrEmpty(cc.PaymentProcessing?.CoinbaseString) ?
            cc.PaymentProcessing?.CoinbaseString.Trim() : "Miningcore";

        if(!string.IsNullOrEmpty(coinbaseString))
            this.scriptSigFinalBytes = new Script(Op.GetPushOp(Encoding.UTF8.GetBytes(coinbaseString))).ToBytes();

        this.Difficulty = new Target(System.Numerics.BigInteger.Parse(BlockTemplate.Target, NumberStyles.HexNumber)).Difficulty;

        this.extraNoncePlaceHolderLength = RavencoinConstants.ExtranoncePlaceHolderLength;
        this.shareMultiplier = shareMultiplier;

        this.coinbaseHasher = coinbaseHasher;
        this.headerHasher = headerHasher;
        this.kawpowHasher = kawpowHasher;

        if(!string.IsNullOrEmpty(BlockTemplate.Target))
            this.blockTargetValue = new uint256(BlockTemplate.Target);
        else
        {
            var tmp = new Target(BlockTemplate.Bits.HexToByteArray());
            this.blockTargetValue = tmp.ToUInt256();
        }

        BuildMerkleBranches();
        BuildCoinbase();

        this.jobParams = new RavencoinJobParams
        {
            Height = BlockTemplate.Height,
            CleanJobs = false
        };
    }

    public new object GetJobParams(bool isNew)
    {
        jobParams.CleanJobs = isNew;
        return jobParams;
    }

    public void PrepareWorkerJob(RavencoinWorkerJob workerJob, out string headerHash)
    {
        workerJob.Job = this;
        workerJob.Height = BlockTemplate.Height;
        workerJob.Bits = BlockTemplate.Bits;
        workerJob.SeedHash = kawpowHasher.SeedHash.ToHexString();
        headerHash = CreateHeaderHash(workerJob);
    }

    private string CreateHeaderHash(RavencoinWorkerJob workerJob)
    {
        var headerHasher = coin.HeaderHasherValue;
        var coinbaseHasher = coin.CoinbaseHasherValue;
        var extraNonce1 = workerJob.ExtraNonce1;

        var coinbase = SerializeCoinbase(extraNonce1);
        Span<byte> coinbaseHash = stackalloc byte[32];
        coinbaseHasher.Digest(coinbase, coinbaseHash);

        var headerBytes = SerializeHeader(coinbaseHash);
        Span<byte> headerHash = stackalloc byte[32];
        headerHasher.Digest(headerBytes, headerHash);
        headerHash.Reverse();

        return headerHash.ToHexString();
    }


    #endregion // API-Surface
}
