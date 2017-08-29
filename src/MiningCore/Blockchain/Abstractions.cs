using System;

namespace MiningCore.Blockchain
{
    public class BlockchainStats
    {
        public string NetworkType { get; set; }
        public double NetworkHashRate { get; set; }
        public double NetworkDifficulty { get; set; }
        public DateTime? LastNetworkBlockTime { get; set; }
        public int BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

    public interface IShare
    {
        /// <summary>
        ///     The pool originating this share from
        /// </summary>
        string PoolId { get; set; }

        /// <summary>
        ///     Who mined it (wallet address)
        /// </summary>
        string Miner { get; }

        /// <summary>
        ///     Who mined it
        /// </summary>
        string Worker { get; }

        /// <summary>
        ///     Mining Software
        /// </summary>
        string UserAgent { get; }

        /// <summary>
        ///     From where was it submitted
        /// </summary>
        string IpAddress { get; }

        /// <summary>
        ///     Share difficulty as submitted by miner
        /// </summary>
        double Difficulty { get; set; }

        /// <summary>
        ///     Stratum difficulty assigned to the miner at the time the share was submitted/accepted (used for payout
        ///     calculations)
        /// </summary>
        double StratumDifficulty { get; set; }

        /// <summary>
        ///     Base difficulty configured for stratum port the submitting worker was connected to
        /// </summary>
        double StratumDifficultyBase { get; set; }

        /// <summary>
        ///     Difficulty relative to a Bitcoin Difficulty 1 Share (used for pool hashrate calculation)
        /// </summary>
        double NormalizedDifficulty { get; set; }

        /// <summary>
        ///     Block this share refers to
        /// </summary>
        long BlockHeight { get; set; }

        /// <summary>
        ///     Block reward after deducting pool fee and donations
        /// </summary>
        decimal BlockReward { get; set; }

        /// <summary>
        ///     If this share presumably resulted in a block
        /// </summary>
        bool IsBlockCandidate { get; set; }

        /// <summary>
        ///     Arbitrary data to be interpreted by the payment processor specialized
        ///     in this coin to verify this block candidate was accepted by the network
        /// </summary>
        string TransactionConfirmationData { get; set; }

        /// <summary>
        ///     Network difficulty at the time the share was submitted (used for some payout schemes like PPLNS)
        /// </summary>
        double NetworkDifficulty { get; set; }

        /// <summary>
        ///     When the share was found
        /// </summary>
        DateTime Created { get; set; }
    }
}
