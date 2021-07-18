using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Extensions;
using Miningcore.Stratum;
using System.Numerics;
using NBitcoin;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJob
    {
        public WorkMessage BlockTemplate { get; private set; }
        public double Difficulty => bTarget.Difficulty;
        public uint Height => BlockTemplate.Height;
        public string JobId { get; protected set; }

        private object[] jobParams;
        private readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IHashAlgorithm hasher = new Blake2b();
        private int extraNonceSize;

        private static readonly uint nBase = (uint) Math.Pow(2, 26);
        private BigInteger N;
        private Target bTarget;
        private BigInteger b;
        private const uint IncreaseStart = 600 * 1024;
        private const uint IncreasePeriodForN = 50 * 1024;
        private const uint NIncreasementHeightMax = 9216000;

        private static BigInteger GetN(ulong height)
        {
            height = Math.Min(4198400, height);
            if(height < 600 * 1024)
            {
                return BigInteger.Pow(2, 26);
            }
            else
            {
                var res = BigInteger.Pow(2, 26);
                var iterationsNumber = (height - 600 * 1024) / 50 * 1024 + 1;
                for(var i = 0ul; i < iterationsNumber; i++)
                    res = res / new BigInteger(100) * new BigInteger(105);
                return res;
            }
        }

        protected bool RegisterSubmit(string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(nTime)
                .Append(nonce)
                .ToString();

            return submissions.TryAdd(key, true);
        }

        public object[] GetJobParams(bool isNew)
        {
            jobParams[^1] = isNew;
            return jobParams;
        }

        public virtual Share ProcessShare(StratumConnection worker, string extraNonce2, string nTime, string nonce)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(extraNonce2), $"{nameof(extraNonce2)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nTime), $"{nameof(nTime)} must not be empty");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce), $"{nameof(nonce)} must not be empty");

            var context = worker.ContextAs<ErgoWorkerContext>();

            // validate nonce
            if(nonce.Length != context.ExtraNonce1.Length + extraNonceSize * 2)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            if(!nonce.StartsWith(context.ExtraNonce1))
                throw new StratumException(StratumError.Other, $"incorrect extraNonce2 in nonce (expected {context.ExtraNonce1}, got {nonce.Substring(0, Math.Min(nonce.Length, context.ExtraNonce1.Length))})");

            // currently unused
            if(nTime == "undefined")
                nTime = string.Empty;

            // dupe check
            if(!RegisterSubmit(nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, $"duplicate share");

            var h = BitConverter.GetBytes(Height).Reverse().Skip(4);
            var coinbaseBuffer = (BlockTemplate.Msg + nonce).HexToByteArray();

            var firstB = new byte[32];
            hasher.Digest(coinbaseBuffer, firstB, 32);
            var formula = new BigInteger(firstB.Skip(24).ToArray(), true, true) % N;
            var i = new byte[4];
            Array.Copy(formula.ToByteArray(), i, formula.ToByteArray().Length);
            i.ReverseInPlace();

            var e = new byte[32];
            hasher.Digest(i.Concat(h).Concat(ErgoConstants.M).ToArray(), e, 32);

            var hash = new byte[32];
            hasher.Digest(e.Skip(1).Concat(coinbaseBuffer).ToArray(), hash, 32);
            var extendedHash = hash.Concat(hash);

            var f = new object[32].Select((x, y) => (new BigInteger(extendedHash.Skip(y).Take(4).ToArray(), true, true) % N).ToByteArray()).Select(x =>
            {
                var buf = new byte[32];
                var item = new byte[4];
                Array.Copy(x, item, x.Length);
                hasher.Digest(item.Reverse().Concat(h).Concat(ErgoConstants.M).ToArray(), buf, 32);
                return new BigInteger(buf.Skip(1).ToArray(), true, true);
            }).Aggregate((x, y) => x + y);

            var buf = new byte[32];
            Array.Copy(f.ToByteArray(), buf, f.ToByteArray().Length);
            hasher.Digest(buf.ReverseInPlace(), buf);

            var fh = new BigInteger(buf, true, true);

            var isBlockCandidate = BigInteger.Compare(b, fh) >= 0;
            var shareDifficulty = (double) (ErgoConstants.BigMaxValue / fh);
            var stratumDifficulty = context.Difficulty;
            var ratio = shareDifficulty / stratumDifficulty;

            // test if share meets at least workers current difficulty
            if(!isBlockCandidate && ratio < 0.99)
            {
                // check if share matched the previous difficulty from before a vardiff retarget
                if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                {
                    ratio = shareDifficulty / context.PreviousDifficulty.Value;

                    if(ratio < 0.99)
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDifficulty})");

                    // use previous difficulty
                    stratumDifficulty = context.PreviousDifficulty.Value;
                }

                else
                    throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDifficulty})");
            }

            var result = new Share
            {
                BlockHeight = Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty / ErgoConstants.ShareMultiplier
            };

            if(isBlockCandidate)
            {
                result.IsBlockCandidate = true;

                result.BlockHash = buf.ToHexString();
            }

            return result;
        }

        public void Init(WorkMessage blockTemplate, int blockVersion, int extraNonceSize, string jobId)
        {
            this.extraNonceSize = extraNonceSize;

            BlockTemplate = blockTemplate;
            JobId = jobId;

            b = BigInteger.Parse(BlockTemplate.B, NumberStyles.Integer);
            bTarget = new Target(b);

            N = GetN(Height); //Som

            jobParams = new object[]
            {
                JobId,
                Height,
                BlockTemplate.Msg,
                string.Empty,
                string.Empty,
                blockVersion,
                null,   // to filled out by ErgoPool.SendJob
                string.Empty,
                false
            };
        }
    }
}
