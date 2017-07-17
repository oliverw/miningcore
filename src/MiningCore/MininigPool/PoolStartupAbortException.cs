using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.MininigPool
{
    public class PoolStartupAbortException : Exception
    {
        public PoolStartupAbortException(string msg) : base(msg)
        {
        }

        public PoolStartupAbortException()
        {
        }
    }
}
