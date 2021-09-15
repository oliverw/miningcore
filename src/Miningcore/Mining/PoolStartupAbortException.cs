using System;

namespace Miningcore.Mining
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
