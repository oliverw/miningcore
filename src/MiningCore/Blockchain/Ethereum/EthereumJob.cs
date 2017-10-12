using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using MiningCore.Crypto.Hashing.Ethash;
using MiningCore.Extensions;
using MiningCore.Stratum;

namespace MiningCore.Blockchain.Ethereum
{
    public class EthereumJob
    {
        public EthereumJob(string id, EthereumBlockTemplate blockTemplate)
        {
            Id = id;
            BlockTemplate = blockTemplate;
        }

        private readonly Dictionary<StratumClient<EthereumWorkerContext>, HashSet<string>> workerNonces = 
            new Dictionary<StratumClient<EthereumWorkerContext>, HashSet<string>>();

        public string Id { get; }
        public EthereumBlockTemplate BlockTemplate { get; }

        private void RegisterNonce(StratumClient<EthereumWorkerContext> worker, string nonce)
        {
            HashSet<string> nonces;

            if (!workerNonces.TryGetValue(worker, out nonces))
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
            lock (workerNonces)
            {
                RegisterNonce(worker, nonce);
            }

            // assemble full-nonce
            var fullNonceHex = worker.Context.ExtraNonce1 + nonce;
            var fullNonce = ulong.Parse(fullNonceHex, NumberStyles.HexNumber);

            // get dag for block
            var dag = await ethash.GetDagAsync(BlockTemplate.Height);

            // compute
            byte[] mixDigest;
            byte[] resultBytes;
            if (!dag.Compute(BlockTemplate.Header.HexToByteArray(), fullNonce, out mixDigest, out resultBytes))
                throw new StratumException(StratumError.MinusOne, "bad hash");

            // Parse the result instead of using the byte array constructor to ensure it ends up as positive integer
            var resultValue = BigInteger.Parse("0" + resultBytes.ToHexString(), NumberStyles.HexNumber);

            // test if share meets at least workers current difficulty
            var shareDiff = (double) BigInteger.Divide(EthereumConstants.BigMaxValue, resultValue) / EthereumConstants.Pow2x32;
            var ratio = shareDiff / worker.Context.Difficulty;
            var isBlockCandidate = resultValue.CompareTo(BlockTemplate.Target) <= 0;

            if (!isBlockCandidate && ratio < 0.99)
            {
                // allow grace period where the previous difficulty from before a vardiff update is also acceptable
                if (worker.Context.VarDiff != null && worker.Context.VarDiff.LastUpdate.HasValue &&
                    worker.Context.PreviousDifficulty.HasValue &&
                    DateTime.UtcNow - worker.Context.VarDiff.LastUpdate.Value < TimeSpan.FromSeconds(15))
                {
                    ratio = shareDiff / worker.Context.PreviousDifficulty.Value;

                    if (ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
            }

            // create share
            var share = new EthereumShare
            {
                BlockHeight = (long)BlockTemplate.Height,
                IpAddress = worker.RemoteEndpoint.Address.ToString(),
                Miner = worker.Context.MinerName,
                Worker = worker.Context.WorkerName,
                UserAgent = worker.Context.UserAgent,
                FullNonceHex = "0x" + fullNonceHex,
                HeaderHash = BlockTemplate.Header,
                MixHash = mixDigest.ToHexString(true),
                IsBlockCandidate = isBlockCandidate,
            };

            if (share.IsBlockCandidate)
                share.TransactionConfirmationData = $"{mixDigest.ToHexString(true)}:{share.FullNonceHex}";

            return share;
        }
    }
}
