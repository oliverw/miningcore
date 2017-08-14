using System;
using CodeContracts;
using MiningCore.Configuration;
using MiningCore.Util;

namespace MiningCore.VarDiff
{
    public class VarDiffManager
    {
        public VarDiffManager(VarDiffConfig varDiffOptions)
        {
            options = varDiffOptions;

            minDiff = options.MinDiff;
            maxDiff = options.MaxDiff;

            var variance = varDiffOptions.TargetTime * (varDiffOptions.VariancePercent / 100.0);
            bufferSize = (int) (varDiffOptions.RetargetTime / varDiffOptions.TargetTime * 4.0);
            tMin = varDiffOptions.TargetTime - variance;
            tMax = varDiffOptions.TargetTime + variance;
        }

        private readonly VarDiffConfig options;
        private readonly int bufferSize;
        private readonly double tMin;
        private readonly double tMax;
        private double minDiff;
        private double maxDiff;

        public double? Update(VarDiffContext ctx, double difficulty, double networkDifficulty)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            lock (ctx)
            {
                maxDiff = networkDifficulty > maxDiff ? options.MaxDiff : networkDifficulty;

                var ts = (DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000) | 0;

                if (ctx.LastRtc == 0)
                {
                    ctx.LastRtc = (long) (ts - options.RetargetTime / 2);
                    ctx.LastTs = ts;
                    ctx.TimeBuffer = new CircularLongBuffer(bufferSize);
                    return null;
                }

                var sinceLast = ts - ctx.LastTs;

                ctx.TimeBuffer.PushBack(sinceLast);
                ctx.LastTs = ts;

                if (ts - ctx.LastRtc < options.RetargetTime && ctx.TimeBuffer.Size > 0)
                    return null;

                ctx.LastRtc = ts;
                var avg = ctx.TimeBuffer.Average();
                var ddiff = options.TargetTime / avg;

                if (avg > tMax && difficulty > minDiff)
                {
                    if (ddiff * difficulty < minDiff)
                        ddiff = minDiff / difficulty;
                }

                else if (avg < tMin)
                {
                    var diffMax = maxDiff;

                    if (ddiff * difficulty > diffMax)
                        ddiff = diffMax / difficulty;
                }

                else
                {
                    return null;
                }

                var newDiff = difficulty * ddiff;
                ctx.TimeBuffer = new CircularLongBuffer(bufferSize);
                return newDiff;
            }
        }
    }
}