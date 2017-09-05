/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using MiningCore.Configuration;
using MiningCore.Util;
using Contract = MiningCore.Contracts.Contract;

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

        private readonly int bufferSize;
        private readonly double minDiff;

        private readonly VarDiffConfig options;
        private readonly double tMax;
        private readonly double tMin;
        private double? maxDiff;

        public double? Update(VarDiffContext ctx, double difficulty, double networkDifficulty)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            lock (ctx)
            {
                maxDiff = maxDiff.HasValue && networkDifficulty > maxDiff ? options.MaxDiff : networkDifficulty;

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
                        ddiff = diffMax.Value / difficulty;
                }

                else
                    return null;

                var newDiff = difficulty * ddiff;
                ctx.TimeBuffer = new CircularLongBuffer(bufferSize);
                return newDiff;
            }
        }
    }
}
