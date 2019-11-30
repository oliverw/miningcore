using System;
using System.Collections.Concurrent;
using NLog;

namespace Miningcore.Native
{
    internal class CryptonightContextStore
    {
        internal CryptonightContextStore(Func<IntPtr> allocator, string logId)
        {
            this.logId = logId.ToUpper();

            // allocate context per CPU
            for(var i = 0; i < contexts.BoundedCapacity; i++)
            {
                contexts.Add(new Lazy<IntPtr>(() =>
                {
                    var result = allocator();

                    return result;
                }));
            }
        }

        private readonly string logId;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        // this holds a finite number of contexts for the cryptonight hashing functions
        // if no context is currently available because all are in use, the thread waits
        private readonly BlockingCollection<Lazy<IntPtr>> contexts = new BlockingCollection<Lazy<IntPtr>>(Environment.ProcessorCount);

        internal Lazy<IntPtr> Lease()
        {
            logger.Debug(() => $"Leasing {logId} context ({contexts.Count})");

            return contexts.Take();
        }

        internal void Return(Lazy<IntPtr> ctx)
        {
            contexts.Add(ctx);

            logger.Debug(() => $"Returned {logId} context ({contexts.Count})");
        }
    }
}
