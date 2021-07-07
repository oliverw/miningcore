using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Crypto.Hashing.Algorithms;
using Miningcore.Extensions;
using Miningcore.Stratum;
using System.Numerics;
using MoreLinq;
using NBitcoin;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJob
    {
        public ErgoBlockTemplate BlockTemplate { get; private set; }
        public double Difficulty { get; private set; }
        public ulong Height => BlockTemplate.Work.Height;
        public string JobId { get; protected set; }

        private object[] jobParams;
        private readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IHashAlgorithm hasher = new Blake2b();
        private BigInteger B;

        private static readonly BigInteger nBase = BigInteger.Pow(2, 26);
        private const ulong IncreaseStart = 600 * 1024;
        private const ulong IncreasePeriodForN = 50 * 1024;
        private const ulong NIncreasementHeightMax = 9216000;
        private static readonly BigInteger a = new(100);
        private static readonly BigInteger b = new(105);

        public static BigInteger CalculateN(ulong height)
        {
            height = Math.Min(NIncreasementHeightMax, height);

            switch (height)
            {
                case < IncreaseStart:
                    return nBase;
                case >= NIncreasementHeightMax:
                    return 2147387550;
            }

            var res = nBase;
            var iterationsNumber = ((height - IncreaseStart) / IncreasePeriodForN) + 1;

            for(var i = 0ul; i < iterationsNumber; i++)
                res = res / a * b;

            return res;
        }

        protected bool RegisterSubmit(string extraNonce1, string extraNonce2, string nTime, string nonce)
        {
            var key = new StringBuilder()
                .Append(extraNonce1)
                .Append(extraNonce2)
                .Append(nTime)
                .Append(nonce)
                .ToString();

            return submissions.TryAdd(key, true);
        }

        protected virtual byte[] SerializeCoinbase(string msg, string extraNonce1, string extraNonce2)
        {
            var msgBytes = msg.HexToByteArray();
            var extraNonce1Bytes = extraNonce1.HexToByteArray();
            var extraNonce2Bytes = extraNonce2.HexToByteArray();

            using(var stream = new MemoryStream())
            {
                stream.Write(msgBytes);
                stream.Write(extraNonce1Bytes);
                stream.Write(extraNonce2Bytes);

                return stream.ToArray();
            }
        }

        private BigInteger[] GenIndexes(byte[] seed, ulong height)
        {
            // hash seed
            Span<byte> hash = stackalloc byte[32];
            hasher.Digest(seed, hash);

            // duplicate
            Span<byte> extendedHash = stackalloc byte[64];
            hash.CopyTo(extendedHash);
            hash.CopyTo(extendedHash.Slice(32, 32));

            // map indexes
            var result = new BigInteger[32];

            for(var i = 0; i < 32; i++)
            {
                var x = BitConverter.ToUInt32(extendedHash.Slice(i, 4)).ToBigEndian();
                var y = CalculateN(height);
                result[i] = x % y;
            }

            return result;
        }

        protected virtual Share ProcessShareInternal(StratumConnection worker, string extraNonce2)
        {
            var context = worker.ContextAs<ErgoWorkerContext>();
            var extraNonce1 = context.ExtraNonce1;

            // hash coinbase
            var coinbase = SerializeCoinbase(BlockTemplate.Work.Msg, extraNonce1, extraNonce2);
            Span<byte> hashResult = stackalloc byte[32];
            hasher.Digest(coinbase, hashResult);

            // calculate i
            var slice = hashResult.Slice(24, 8);
            var tmp2 = new BigInteger(slice, true, true) % CalculateN(Height);
            var i = tmp2.ToByteArray(false, true).PadFront(0, 4);

            // calculate e
            var h = new BigInteger(Height).ToByteArray(true, true).PadFront(0, 4);
            var ihM = i.Concat(h).Concat(ErgoConstants.M).ToArray();
            hasher.Digest(ihM, hashResult);
            var e = hashResult[1..].ToArray();

            // calculate j
            var eCoinbase = e.Concat(coinbase).ToArray();
            var jTmp = GenIndexes(eCoinbase, Height);
            var j = jTmp.Select(x => x.ToByteArray(true, true).PadFront(0, 4)).ToArray();

            // calculate f
            var f = j.Select(x =>
            {
                var buf = x.Concat(h).Concat(ErgoConstants.M).ToArray();

                // hash it
                Span<byte> hash = stackalloc byte[32];
                hasher.Digest(buf, hash);

                // extract 31 bytes at end
                return new BigInteger(hash[1..], true, true);
            }).Aggregate((x, y) => x + y);

            // calculate fH
            var blockHash = f.ToByteArray(true, true).PadFront(0, 32);
            hasher.Digest(blockHash, hashResult);
            var fh = new BigInteger(hashResult, true, true);

            // diff check
            var stratumDifficulty = context.Difficulty;
            var isLowDifficulty = fh / new BigInteger(context.Difficulty) > B;

            // check if the share meets the much harder block difficulty (block candidate)
            var isBlockCandidate = B >= fh;

            // test if share meets at least workers current difficulty
            if(!isBlockCandidate && isLowDifficulty)
                throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share");

            var result = new Share
            {
                BlockHeight = (long) Height,
                NetworkDifficulty = Difficulty,
                Difficulty = stratumDifficulty
            };

            if(isBlockCandidate)
            {
                result.IsBlockCandidate = true;

                result.BlockHash = blockHash.ToHexString();
            }

            return result;
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
            if(nonce.Length != 16)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            // dupe check
            if(!RegisterSubmit(context.ExtraNonce1, extraNonce2, nTime, nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            return ProcessShareInternal(worker, extraNonce2);
        }

        public void Init(ErgoBlockTemplate blockTemplate, string jobId)
        {
            BlockTemplate = blockTemplate;
            JobId = jobId;
            B = BigInteger.Parse(BlockTemplate.Work.B, NumberStyles.Integer);
            Difficulty = new Target(B).Difficulty;

            jobParams = new object[]
            {
                JobId,
                Height,
                BlockTemplate.Work.Msg,
                string.Empty,
                string.Empty,
                BlockTemplate.Info.Parameters.BlockVersion,
                B,
                string.Empty,
                false
            };
        }
    }
}
