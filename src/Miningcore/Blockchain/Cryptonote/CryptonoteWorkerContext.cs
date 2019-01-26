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

using System.Collections.Generic;
using System.Linq;
using Miningcore.Mining;

namespace Miningcore.Blockchain.Cryptonote
{
    public class CryptonoteWorkerContext : WorkerContextBase
    {
        /// <summary>
        /// Usually a wallet address
        /// NOTE: May include paymentid (seperated by a dot .)
        /// </summary>
        public string Miner { get; set; }

        /// <summary>
        /// Arbitrary worker identififer for miners using multiple rigs
        /// </summary>
        public string Worker { get; set; }

        private List<CryptonoteWorkerJob> validJobs { get; } = new List<CryptonoteWorkerJob>();

        public void AddJob(CryptonoteWorkerJob job)
        {
            validJobs.Insert(0, job);

            while(validJobs.Count > 4)
                validJobs.RemoveAt(validJobs.Count - 1);
        }

        public CryptonoteWorkerJob FindJob(string jobId)
        {
            return validJobs.FirstOrDefault(x => x.Id == jobId);
        }
    }
}
