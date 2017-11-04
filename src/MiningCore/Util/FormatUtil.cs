using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Util
{
    public static class FormatUtil
    {
        public static readonly string[] HashRateUnits = { " KH/s", " MH/s", " GH/s", " TH/s", " PH/s" };
        public static readonly string[] DifficultyUnits = { " K", " M", " G", " T", " P" };

        public static string FormatHashRate(double hashrate)
        {
            var i = -1;

            do
            {
                hashrate = hashrate / 1024;
                i++;
            } while(hashrate > 1024 && i < HashRateUnits.Length - 1);

            return (int) Math.Abs(hashrate) + HashRateUnits[i];
        }

        public static string FormatDifficulty(double difficulty)
        {
            var i = -1;

            do
            {
                difficulty = difficulty / 1024;
                i++;
            } while(difficulty > 1024);
            return (int) Math.Abs(difficulty) + DifficultyUnits[i];
        }
    }
}
