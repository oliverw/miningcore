using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using MiningCore.Crypto.Hashing.Ethash;
using MiningCore.Extensions;
using MiningCore.Stratum;
using NBitcoin;
using NLog;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(string id, EthereumBlockTemplate blockTemplate, ILogger logger)
        {
            Id = id;
            BlockTemplate = blockTemplate;
            this.logger = logger;

            var target = blockTemplate.Target;
            if (target.StartsWith("0x"))
                target = target.Substring(2);

            blockTarget = new uint256(target.HexToByteArray().ReverseArray());
        }

        private readonly Dictionary<StratumClient, HashSet<string>> workerNonces =
            new Dictionary<StratumClient, HashSet<string>>();

        public string Id { get; }
        public EthereumBlockTemplate BlockTemplate { get; }
        private readonly uint256 blockTarget;
        private readonly ILogger logger;

        private void RegisterNonce(StratumClient worker, string nonce)
        {
            var nonceLower = nonce.ToLower();

            if (!workerNonces.TryGetValue(worker, out var nonces))
            {
                nonces = new HashSet<string>(new[] { nonceLower });
                workerNonces[worker] = nonces;
            }

            else
            {
                if (nonces.Contains(nonceLower))
                    throw new StratumException(StratumError.MinusOne, "duplicate share");

                nonces.Add(nonceLower);
            }
        }

        public async Task<(Share Share, string FullNonceHex, string HeaderHash, string MixHash)> ProcessShareAsync(StratumClient worker, string nonce, EthashFull ethash)
        {
            // duplicate nonce?
            lock(workerNonces)
            {
                RegisterNonce(worker, nonce);
            }

            // assemble full-nonce
            var context = worker.GetContextAs<EthereumWorkerContext>();
            var fullNonceHex = context.ExtraNonce1 + nonce;
            var fullNonce = ulong.Parse(fullNonceHex, NumberStyles.HexNumber);

            // get dag for block
            var dag = await ethash.GetDagAsync(BlockTemplate.Height, logger);

            // compute
            if (!dag.Compute(logger, BlockTemplate.Header.HexToByteArray(), fullNonce, out var mixDigest, out var resultBytes))
                throw new StratumException(StratumError.MinusOne, "bad hash");

            resultBytes.ReverseArray();

            // test if share meets at least workers current difficulty
            var resultValue = new uint256(resultBytes);
            var resultValueBig = resultBytes.ToBigInteger();
            var shareDiff = (double) BigInteger.Divide(EthereumConstants.BigMaxValue, resultValueBig) / EthereumConstants.Pow2x32;
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;
            var isBlockCandidate = resultValue <= blockTarget;

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

            // create share
            var share = new Share
            {
                BlockHeight = (long) BlockTemplate.Height,
                IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
                Miner = context.MinerName,
                Worker = context.WorkerName,
                UserAgent = context.UserAgent,
                IsBlockCandidate = isBlockCandidate,
                Difficulty = stratumDifficulty * EthereumConstants.Pow2x32,
                BlockHash = mixDigest.ToHexString(true)     // OW: is this correct?
            };

            if (share.IsBlockCandidate)
            {
                fullNonceHex = "0x" + fullNonceHex;
                var headerHash = BlockTemplate.Header;
                var mixHash = mixDigest.ToHexString(true);

                share.TransactionConfirmationData = $"{mixDigest.ToHexString(true)}:{fullNonceHex}";

                return (share, fullNonceHex, headerHash, mixHash);
            }

            return (share, null, null, null);
        }
    }
}
