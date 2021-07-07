using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NBitcoin;

// ReSharper disable InconsistentNaming

namespace Miningcore.Blockchain.Ergo
{
    public static class ErgoConstants
    {
        public const uint DiffMultiplier = 256;
        public static double Pow2x26 = Math.Pow(2, 26);

        public const decimal SmallestUnit = 1000000000;

        public static Regex RegexChain = new("ergo-([^-]+)-.+", RegexOptions.Compiled);

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
    }
}
