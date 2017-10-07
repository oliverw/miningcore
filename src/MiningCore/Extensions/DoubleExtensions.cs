using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Extensions
{
    public static class DoubleExtensions
    {
        private const double P3 = 0.001;
        private const double P4 = 0.0001;

        public static bool EqualsDigitPrecision3(this double left, double right)
        {
            return Math.Abs(left - right) < P3;
        }

        public static bool EqualsDigitPrecision4(this double left, double right)
        {
            return Math.Abs(left - right) < P4;
        }
    }
}
