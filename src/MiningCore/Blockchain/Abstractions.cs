/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;

namespace MiningCore.Blockchain
{
    public class BlockchainStats
    {
        public string NetworkType { get; set; }
        public double NetworkHashrate { get; set; }
        public double NetworkDifficulty { get; set; }
        public DateTime? LastNetworkBlockTime { get; set; }
        public long BlockHeight { get; set; }
        public int ConnectedPeers { get; set; }
        public string RewardType { get; set; }
    }

    public interface IExtraNonceProvider
    {
        string Next();
    }

    public interface IShare
    {
        /// <summary>
        /// The pool originating this share from
        /// </summary>
        string PoolId { get; set; }

        /// <summary>
        /// Who mined it (wallet address)
        /// </summary>
        string Miner { get; }

        /// <summary>
        /// Who mined it
        /// </summary>
        string Worker { get; }

        /// <summary>
        /// Extra information for payout processing
        /// </summary>
        string PayoutInfo { get; set; }

        /// <summary>
        /// Mining Software
        /// </summary>
        string UserAgent { get; }

        /// <summary>
        /// From where was it submitted
        /// </summary>
        string IpAddress { get; }

        /// <summary>
        /// Submission source (pool, external stratum etc)
        /// </summary>
        string Source { get; set; }

        /// <summary>
        /// Stratum difficulty assigned to the miner at the time the share was submitted/accepted (used for payout
        /// calculations)
        /// </summary>
        double Difficulty { get; set; }

        /// <summary>
        /// Block this share refers to
        /// </summary>
        long BlockHeight { get; set; }

        /// <summary>
        /// Block reward after deducting pool fee and donations
        /// </summary>
        decimal BlockReward { get; set; }

        /// <summary>
        /// Block hash
        /// </summary>
        string BlockHash { get; set; }

        /// <summary>
        /// If this share presumably resulted in a block
        /// </summary>
        bool IsBlockCandidate { get; set; }

        /// <summary>
        /// Arbitrary data to be interpreted by the payment processor specialized
        /// in this coin to verify this block candidate was accepted by the network
        /// </summary>
        string TransactionConfirmationData { get; set; }

        /// <summary>
        /// Network difficulty at the time the share was submitted (used for some payout schemes like PPLNS)
        /// </summary>
        double NetworkDifficulty { get; set; }

        /// <summary>
        /// When the share was found
        /// </summary>
        DateTime Created { get; set; }
    }
}
