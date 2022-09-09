using System.Globalization;
using System.Numerics;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.Stratum;
using NBitcoin;
using NLog;

namespace Miningcore.Blockchain.Ethereum;

public class EthereumJob
{
    public EthereumJob(string id, EthereumBlockTemplate blockTemplate, ILogger logger)
    {
        Id = id;
        BlockTemplate = blockTemplate;
        this.logger = logger;

        var target = blockTemplate.Target;
        if(target.StartsWith("0x"))
            target = target.Substring(2);

        blockTarget = new uint256(target.HexToReverseByteArray());
    }

    private readonly Dictionary<string, HashSet<string>> workerNonces = new();

    public string Id { get; }
    public EthereumBlockTemplate BlockTemplate { get; }
    private readonly uint256 blockTarget;
    private readonly ILogger logger;

    public record SubmitResult(Share Share, string FullNonceHex = null, string HeaderHash = null, string MixHash = null);

    private void RegisterNonce(StratumConnection worker, string nonce)
    {
        var nonceLower = nonce.ToLower();

        if(!workerNonces.TryGetValue(worker.ConnectionId, out var nonces))
        {
            nonces = new HashSet<string>(new[] { nonceLower });
            workerNonces[worker.ConnectionId] = nonces;
        }

        else
        {
            if(nonces.Contains(nonceLower))
                throw new StratumException(StratumError.MinusOne, "duplicate share");

            nonces.Add(nonceLower);
        }
    }

    public async Task<SubmitResult> ProcessShareAsync(StratumConnection worker,
        string workerName, string fullNonceHex, EthashFull ethash, CancellationToken ct)
    {
        // dupe check
        lock(workerNonces)
        {
            RegisterNonce(worker, fullNonceHex);
        }

        var context = worker.ContextAs<EthereumWorkerContext>();

        if(!ulong.TryParse(fullNonceHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fullNonce))
            throw new StratumException(StratumError.MinusOne, "bad nonce " + fullNonceHex);

        // get dag for block
        var dag = await ethash.GetDagAsync(BlockTemplate.Height, logger, CancellationToken.None);

        // compute
        if(!dag.Compute(logger, BlockTemplate.Header.HexToByteArray(), fullNonce, out var mixDigest, out var resultBytes))
            throw new StratumException(StratumError.MinusOne, "bad hash");

        // test if share meets at least workers current difficulty
        resultBytes.ReverseInPlace();
        var resultValue = new uint256(resultBytes);
        var resultValueBig = resultBytes.AsSpan().ToBigInteger();
        var shareDiff = (double) BigInteger.Divide(EthereumConstants.BigMaxValue, resultValueBig) / EthereumConstants.Pow2x32;
        var stratumDifficulty = context.Difficulty;
        var ratio = shareDiff / stratumDifficulty;
        var isBlockCandidate = resultValue <= blockTarget;

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

        var share = new Share
        {
            BlockHeight = (long) BlockTemplate.Height,
            IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
            Miner = context.Miner,
            Worker = workerName,
            UserAgent = context.UserAgent,
            IsBlockCandidate = isBlockCandidate,
            Difficulty = stratumDifficulty * EthereumConstants.Pow2x32
        };

        if(share.IsBlockCandidate)
        {
            fullNonceHex = "0x" + fullNonceHex;
            var headerHash = BlockTemplate.Header;
            var mixHash = mixDigest.ToHexString(true);

            share.TransactionConfirmationData = "";

            return new SubmitResult(share, fullNonceHex, headerHash, mixHash);
        }

        return new SubmitResult(share);
    }

    public object[] GetJobParamsForStratum()
    {
        return new object[]
        {
            Id,
            BlockTemplate.Seed.StripHexPrefix(),
            BlockTemplate.Header.StripHexPrefix(),
            true
        };
    }

    public object[] GetWorkParamsForStratum(EthereumWorkerContext context)
    {
        // https://github.com/edsonayllon/Stratum-Implementation-For-Pantheon
        var workerTarget = BigInteger.Divide(EthereumConstants.BigMaxValue, new BigInteger(context.Difficulty * EthereumConstants.Pow2x32));
        var workerTargetString = workerTarget.ToByteArray(false, true).ToHexString(true);

        return new object[]
        {
            BlockTemplate.Header,
            BlockTemplate.Seed,
            workerTargetString,
        };
    }
}
