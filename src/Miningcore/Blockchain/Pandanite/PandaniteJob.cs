using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Miningcore.Contracts;
using Miningcore.Stratum;
using NBitcoin;

namespace Miningcore.Blockchain.Pandanite
{
    public class PandaniteJob
    {
        public uint Id { get; set; }
        public string JobId { get; set; }
        public ulong Timestamp { get; set; }
        public int ChallengeSize { get; set; }
        public byte[] LastHash { get; set; }
        public byte[] RootHash { get; set; }
        public byte[] Nonce { get; set; }
        public List<Transaction> Transactions { get; set; }
        public MiningProblem Problem { get; set; }
        protected readonly ConcurrentDictionary<string, bool> submissions = new(StringComparer.OrdinalIgnoreCase);

        public bool RegisterSubmit(string nonce)
        {
            return submissions.TryAdd(nonce, true);
        }

        public Transaction GetMiningFeeTransaction()
        {
            return Transactions
                .Where(x => x.isTransactionFee)
                .FirstOrDefault();
        }

        public unsafe (Share Share, byte[] Block) ProcessShare(StratumConnection worker, string nonce)
        {
            Contract.RequiresNonNull(worker);
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(nonce));
            
            // validate nonce
            if(nonce.Length != 64)
                throw new StratumException(StratumError.Other, "incorrect size of nonce");

            // dupe check
            if(!RegisterSubmit(nonce))
                throw new StratumException(StratumError.DuplicateShare, "duplicate share");

            var context = worker.ContextAs<PandaniteWorkerContext>();

            Span<byte> concat = new byte[64];
            Span<byte> hash = new byte[119] { 
                36, 80, 70, 50, 36, 46, 46, 101, 46, 46, 
                46, 46, 46, 46, 46, 46, 46, 46, 46, 46, 
                46, 46, 46, 46, 46, 46, 46, 46, 46, 36, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
                0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var nBytes = nonce.ToByteArray();

            for (int i = 0; i < 32; i++) {
                concat[i] = Nonce[i];
                concat[i + 32] = nBytes[i];
            }

            var stratumDifficulty = context.Difficulty;

            var diffInt = ((int)Math.Floor(stratumDifficulty));
            var diffFrac = stratumDifficulty - diffInt;

            var stratumTarget = (ulong.MaxValue >> diffInt) - ((ulong.MaxValue >> (diffInt + 1)) * diffFrac);
            var blockTargetValue = ulong.MaxValue >> ChallengeSize;

            using (SHA256 sha256 = SHA256.Create())
            fixed (byte* ptr = concat, hashPtr = hash)
            {
                Unmanaged.pf_newhash(ptr, 64, hashPtr);
                var sha256Hash = sha256.ComputeHash(hash.ToArray());

                var asLong = BitConverter.ToInt64(sha256Hash.Take(8).ToArray(), 0);
                var shareDiff = (ulong)IPAddress.NetworkToHostOrder(asLong);

                var valid = shareDiff <= stratumTarget;

                // check if the share meets the much harder block difficulty (block candidate)
                var isBlockCandidate = shareDiff <= blockTargetValue;

                if (!isBlockCandidate && !valid) {
                    // check if share matched the previous difficulty from before a vardiff retarget
                    if(context.VarDiff?.LastUpdate != null && context.PreviousDifficulty.HasValue)
                    {
                        var prevDiffInt = ((int)Math.Floor(context.PreviousDifficulty.Value));
                        var prevDiffFrac = context.PreviousDifficulty.Value - prevDiffInt;

                        var prevDifficulty = (ulong.MaxValue >> prevDiffInt) - ((ulong.MaxValue >> (prevDiffInt + 1)) * prevDiffFrac);
                        
                        valid = shareDiff <= prevDifficulty;

                        if(!valid)
                            throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");

                        // use previous difficulty
                        stratumDifficulty = context.PreviousDifficulty.Value;
                    }
                    else 
                    {
                        throw new StratumException(StratumError.LowDifficultyShare, $"low difficulty share ({shareDiff})");
                    }
                }

                var result = new Share
                {
                    BlockHeight = Id,
                    NetworkDifficulty = Math.Pow(2, ChallengeSize),
                    Difficulty = Math.Pow(2, stratumDifficulty)
                };

                if(isBlockCandidate)
                {
                    result.IsBlockCandidate = true;

                    using var stream = new MemoryStream();
                    using var writer = new BinaryWriter(stream);

                    var tree = new MerkleTree(Transactions);

                    writer.Write(Id);
                    writer.Write(Timestamp);
                    writer.Write(ChallengeSize);
                    writer.Write(Transactions.Count);
                    writer.Write(LastHash);
                    writer.Write(RootHash);
                    writer.Write(nonce.ToByteArray());

                    foreach (var transaction in Transactions)
                    {
                        var signature = string.IsNullOrEmpty(transaction.signature) ? 
                            new byte[64] {  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }: 
                            transaction.signature.ToByteArray();
                        writer.Write(signature);
                        
                        var signingKey = string.IsNullOrEmpty(transaction.signingKey) ? 
                            new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }: 
                            transaction.signingKey.ToByteArray();
                        writer.Write(signingKey);

                        writer.Write(ulong.Parse(transaction.timestamp));

                        writer.Write(transaction.to.ToByteArray());
                        writer.Write(transaction.amount);
                        writer.Write(transaction.fee);
                        writer.Write(Convert.ToUInt32(transaction.isTransactionFee));
                    }

                    writer.Flush();
                    stream.Flush();
                    stream.Position = 0;

                    return (result, stream.ToArray());
                }

                return (result, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int calculateLeadingZeroBits(byte[] hash) {
            var cBits = 0;
            var breakAt = 0;

            for (int i = 0; i < hash.Length; i++) {
                if (hash[i] != 0) {
                    breakAt = i;
                    break;
                }

                cBits += sizeof(byte) * 8;
            }

            var shifts = 0;
            var uValue = hash[breakAt];
            while(uValue != 0)
            {
                uValue = (byte)(uValue >> 1);
                shifts++;
            }


            return cBits + (8 - shifts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool checkLeadingZeroBits2(byte[] hash, int challengeSize) {
            int challengeBytes = challengeSize / 8;
            int remainingBits = challengeSize - (8 * challengeBytes);
            int remainingValue = 255 >> remainingBits;

            for (int i = 0; i < challengeBytes; i++) {
                if (hash[i] != 0) return false;
            }

            return hash[challengeBytes] <= remainingValue;
        }
    }
}