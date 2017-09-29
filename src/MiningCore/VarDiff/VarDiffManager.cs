/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)
Authors: shtse8 (github)

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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using MiningCore.Configuration;
using MiningCore.Mining;
using MiningCore.Util;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.VarDiff
{
    public class VarDiffManager
    {
        public VarDiffManager(VarDiffConfig varDiffOptions)
        {
            options = varDiffOptions;

            var variance = varDiffOptions.TargetTime * (varDiffOptions.VariancePercent / 100.0);
            tMin = varDiffOptions.TargetTime - variance;
            tMax = varDiffOptions.TargetTime + variance;
        }

        private readonly VarDiffConfig options;
        private readonly double tMax;
        private readonly double tMin;

        public double? Update(WorkerContextBase ctx, IList<long> shares)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));

            lock (ctx)
            {
                var difficulty = ctx.Difficulty;
                var minDiff = options.MinDiff;
                var maxDiff = options.MaxDiff ?? double.MaxValue;
                var desiredShares = Math.Floor(options.RetargetTime / options.TargetTime);
                double? newDiff = null;

                if ((shares.Count > 0 && ctx.VarDiff.LastShareTs.HasValue) || shares.Count > 1)
                {
                    // make relative to each other
                    var tsRelative = new List<long>();

                    // first value is relative to last value of previous buffer
                    if (ctx.VarDiff.LastShareTs.HasValue)
                        tsRelative.Add(shares[0] - ctx.VarDiff.LastShareTs.Value);

                    for (int i = 1; i < shares.Count; i++)
                        tsRelative.Add(shares[i] - shares[i-1]);

                    var avg = tsRelative.Average();

                    // add penalty for missing shares
                    if (tsRelative.Count < desiredShares - 1)
                        avg += options.TargetTime * 0.5 * ((double)tsRelative.Count / (desiredShares - 1));

                    // re-target if outside bounds
                    if (avg < tMin || avg > tMax)
                    {
                        var change = options.TargetTime / avg;
                        newDiff = difficulty * change;

                        Debug.WriteLine(newDiff.Value);
                    }

                    // store
                    ctx.VarDiff.LastShareTs = shares.Last();
                    ctx.VarDiff.SilenceCount = 0;
                }

                else
                {
                    ctx.VarDiff.SilenceCount++;

                    // radical measures if there were no shares submitted at all within the buffer window
                    newDiff = difficulty / Math.Pow(2, ctx.VarDiff.SilenceCount);
                }

                if (newDiff.HasValue)
                {
                    // clamp to min/max
                    if (newDiff < minDiff)
                        newDiff = minDiff;
                    else if (newDiff > maxDiff)
                        newDiff = maxDiff;
                }

                if (newDiff.HasValue && (newDiff.Value < 0.00001 || double.IsNegativeInfinity(newDiff.Value) ||
                                         double.IsPositiveInfinity(newDiff.Value)))
                    ;

                return newDiff;
            }
        }
    }
}
