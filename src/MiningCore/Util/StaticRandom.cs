using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MiningCore.Util
{
    public static class StaticRandom
    {
        static int seed = Environment.TickCount;

        private static readonly ThreadLocal<Random> random =
            new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

        public static int Next()
        {
            return random.Value.Next();
        }

        public static int Next(int n)
        {
            return random.Value.Next(n);
        }
    }
}
