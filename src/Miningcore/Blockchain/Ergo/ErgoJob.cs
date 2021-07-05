using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Miningcore.Blockchain.Cryptonote;

namespace Miningcore.Blockchain.Ergo
{
    public class ErgoJob
    {
        public string PrevHash { get; set; }

        public void PrepareWorkerJob(CryptonoteWorkerJob workerJob, out string blob, out string target)
        {
            throw new NotImplementedException();
        }

        public object GetJobParams(bool isNew)
        {
            throw new NotImplementedException();
        }
    }
}
