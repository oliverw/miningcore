using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using MiningCore.Crypto.Hashing.Ethash;
using MiningCore.Extensions;
using MiningCore.Stratum;
using NBitcoin;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(string id, EthereumBlockTemplate blockTemplate)
        {
            Id = id;
            BlockTemplate = blockTemplate;

            var target = blockTemplate.Target;
            if (target.StartsWith("0x"))
                target = target.Substring(2);

            blockTarget = new uint256(target.HexToByteArray().ReverseArray());
        }

        private readonly Dictionary<StratumClient<EthereumWorkerContext>, HashSet<string>> workerNonces =
            new Dictionary<StratumClient<EthereumWorkerContext>, HashSet<string>>();

        public string Id { get; }
        public EthereumBlockTemplate BlockTemplate { get; }
        private readonly uint256 blockTarget;

        private void RegisterNonce(StratumClient<EthereumWorkerContext> worker, string nonce)
        {
            if (!workerNonces.TryGetValue(worker, out var nonces))
            {
                nonces = new HashSet<string>(new[] { nonce });
                workerNonces[worker] = nonces;
            }

            else
            {
                if (nonces.Contains(nonce))
                    throw new StratumException(StratumError.MinusOne, "duplicate share");

                nonces.Add(nonce);
            }
        }

        public async Task<EthereumShare> ProcessShareAsync(StratumClient<EthereumWorkerContext> worker, string nonce, EthashFull ethash)
        {
            // duplicate nonce?
            lock(workerNonces)
            {
                RegisterNonce(worker, nonce);
            }

            // assemble full-nonce
            var fullNonceHex = worker.Context.ExtraNonce1 + nonce;
            var fullNonce = ulong.Parse(fullNonceHex, NumberStyles.HexNumber);

            // get dag for block
            var dag = await ethash.GetDagAsync(BlockTemplate.Height);

            // compute
            if (!dag.Compute(BlockTemplate.Header.HexToByteArray(), fullNonce, out var mixDigest, out var resultBytes))
                throw new StratumException(StratumError.MinusOne, "bad hash");

            resultBytes.ReverseArray();

            // test if share meets at least workers current difficulty
            var resultValue = new uint256(resultBytes);
            var shareDiff = (double) BigInteger.Divide(EthereumConstants.BigMaxValue, new BigInteger(resultBytes)) / EthereumConstants.Pow2x32;
            var stratumDifficulty = worker.Context.Difficulty;
            var ratio = shareDiff / stratumDifficulty;
            var isBlockCandidate = resultValue <= blockTarget;

            if (!isBlockCandidate && ratio < 0.99)
            {
                // check if share matched the previous difficulty from before a vardiff retarget
                if (worker.Context.VarDiff?.LastUpdate != null && worker.Context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDiff / worker.Context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                    // use previous difficulty
                    stratumDifficulty = worker.Context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            // create share
            var share = new EthereumShare
            {
                BlockHeight = (long) BlockTemplate.Height,
                IpAddress = worker.RemoteEndpoint?.Address?.ToString(),
                Miner = worker.Context.MinerName,
                Worker = worker.Context.WorkerName,
                UserAgent = worker.Context.UserAgent,
                FullNonceHex = "0x" + fullNonceHex,
                HeaderHash = BlockTemplate.Header,
                MixHash = mixDigest.ToHexString(true),
                IsBlockCandidate = isBlockCandidate,
                Difficulty = stratumDifficulty * EthereumConstants.Pow2x32,
            };

            if (share.IsBlockCandidate)
                share.TransactionConfirmationData = $"{mixDigest.ToHexString(true)}:{share.FullNonceHex}";

            return share;
        }
    }
}
