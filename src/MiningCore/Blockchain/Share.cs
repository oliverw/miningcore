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
    public class Share
    {
        /// <summary>
        /// The pool originating this share from
        /// </summary>
        public string PoolId { get; set; }

        /// <summary>
        /// Who mined it (wallet address)
        /// </summary>
        public string Miner { get; set; }

        /// <summary>
        /// Who mined it
        /// </summary>
        public string Worker { get; set; }

        /// <summary>
        /// Extra information for payout processing
        /// </summary>
        public string PayoutInfo { get; set; }

        /// <summary>
        /// Mining Software
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// From where was it submitted
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Submission source (pool, external stratum etc)
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Stratum difficulty assigned to the miner at the time the share was submitted/accepted (used for payout
        /// calculations)
        /// </summary>
        public double Difficulty { get; set; }

        /// <summary>
        /// Block this share refers to
        /// </summary>
        public long BlockHeight { get; set; }

        /// <summary>
        /// Block reward after deducting pool fee and donations
        /// </summary>
        public decimal BlockReward { get; set; }

        /// <summary>
        /// Block hash
        /// </summary>
        public string BlockHash { get; set; }

        /// <summary>
        /// If this share presumably resulted in a block
        /// </summary>
        public bool IsBlockCandidate { get; set; }

        /// <summary>
        /// Arbitrary data to be interpreted by the payment processor specialized
        /// in this coin to verify this block candidate was accepted by the network
        /// </summary>
        public string TransactionConfirmationData { get; set; }

        /// <summary>
        /// Network difficulty at the time the share was submitted (used for some payout schemes like PPLNS)
        /// </summary>
        public double NetworkDifficulty { get; set; }

        /// <summary>
        /// When the share was found
        /// </summary>
        public DateTime Created { get; set; }
    }
}
