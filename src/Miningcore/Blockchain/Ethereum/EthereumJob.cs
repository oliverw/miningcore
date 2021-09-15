using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Crypto.Hashing.Ethash;
using Miningcore.Extensions;
using Miningcore.Stratum;
using NBitcoin;
using NLog;

namespace Miningcore.Blockchain.Ethereum
{
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

        private readonly Dictionary<StratumConnection, HashSet<string>> workerNonces =
             new();

        public string Id { get; }
        public EthereumBlockTemplate BlockTemplate { get; }
        private readonly uint256 blockTarget;
        private readonly ILogger logger;

        private void RegisterNonce(StratumConnection worker, string nonce)
        {
            var nonceLower = nonce.ToLower();

            if(!workerNonces.TryGetValue(worker, out var nonces))
            {
                nonces = new HashSet<string>(new[] { nonceLower });
                workerNonces[worker] = nonces;
            }

            else
            {
                if(nonces.Contains(nonceLower))
                    throw new StratumException(StratumError.MinusOne, "duplicate share");

                nonces.Add(nonceLower);
            }
        }

        public async ValueTask<(Share Share, string FullNonceHex, string HeaderHash, string MixHash)> ProcessShareAsync(
            StratumConnection worker, string nonce, EthashFull ethash, CancellationToken ct)
        {
            // duplicate nonce?
            lock(workerNonces)
            {
                RegisterNonce(worker, nonce);
            }

            // assemble full-nonce
            var context = worker.ContextAs<EthereumWorkerContext>();
            var fullNonceHex = context.ExtraNonce1 + nonce;

            if(!ulong.TryParse(fullNonceHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fullNonce))
                throw new StratumException(StratumError.MinusOne, "bad nonce " + fullNonceHex);

            // get dag for block
            var dag = await ethash.GetDagAsync(BlockTemplate.Height, logger, ct);

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

            // create share
            var share = new Share
            {
                BlockHeight = (long) BlockTemplate.Height,
                IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
                Miner = context.Miner,
                Worker = context.Worker,
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

                return (share, fullNonceHex, headerHash, mixHash);
            }

            return (share, null, null, null);
        }
    }
}
