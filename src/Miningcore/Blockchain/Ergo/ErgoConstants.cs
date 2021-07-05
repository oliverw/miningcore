using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;

// ReSharper disable InconsistentNaming

namespace Miningcore.Blockchain.Ergo
{
    public static class ErgoConstants
    {
        public static readonly BigInteger NBase = BigInteger.Pow(2, 26);

        public const ulong IncreaseStart = 600 * 1024;
        public const ulong IncreasePeriodForN = 50 * 1024;
        public const ulong NIncreasementHeightMax = 9216000;

        private static readonly BigInteger a = new(100);
        private static readonly BigInteger b = new(105);

        public static readonly byte[] M = Enumerable.Range(0, 1024)
            .SelectMany(x =>
            {
                const double max = 4294967296d;
                var top = (uint) Math.Floor(x / max);
                var rem  = (uint) (x - top * max);

                var result = BitConverter.GetBytes(rem)
                    .Concat(BitConverter.GetBytes(top))
                    .Reverse()
                    .ToArray();

                return result;
            }).ToArray();

        public static BigInteger N(ulong height)
        {
            height = Math.Min(NIncreasementHeightMax, height);

            if(height < IncreaseStart)
                return NBase;

            if(height >= NIncreasementHeightMax)
                return 2147387550;

            var res = NBase;
            var iterationsNumber = ((height - IncreaseStart) / IncreasePeriodForN) + 1;

            for(var i = 0ul; i < iterationsNumber; i++)
                res = res / a * b;

            return res;
        }
    }
}
