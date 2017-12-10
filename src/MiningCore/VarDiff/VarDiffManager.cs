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
using System.Linq;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Mining;
using MiningCore.Time;
using NLog;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.VarDiff
{
    public class VarDiffManager
    {
        public VarDiffManager(VarDiffConfig varDiffOptions, IMasterClock clock)
        {
            options = varDiffOptions;
            this.clock = clock;

            var variance = varDiffOptions.TargetTime * (varDiffOptions.VariancePercent / 100.0);
            tMin = varDiffOptions.TargetTime - variance;
            tMax = varDiffOptions.TargetTime + variance;
            maxJump = varDiffOptions.MaxDelta ?? 10000;
        }

        private readonly VarDiffConfig options;
        private readonly IMasterClock clock;
        private readonly double tMax;
        private readonly double tMin;
        private readonly double maxJump;

        public double? Update(WorkerContextBase ctx, IList<long> shares, string connectionId, ILogger logger)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            logger.Debug(() => $"Update");

            lock (ctx)
            {
                var difficulty = ctx.Difficulty;
                var minDiff = options.MinDiff;
                var maxDiff = options.MaxDiff ?? double.MaxValue;
                double? newDiff = null;

                if ((shares.Count > 0 && ctx.VarDiff.LastShareTs.HasValue) || shares.Count > 1)
                {
                    // make timestamps relative to each other
                    var tsRelative = new List<long>();

                    // first value is relative to last value of previous buffer
                    if (ctx.VarDiff.LastShareTs.HasValue)
                        tsRelative.Add(Math.Max(0, shares[0] - ctx.VarDiff.LastShareTs.Value));

                    for(var i = 1; i < shares.Count; i++)
                        tsRelative.Add(Math.Max(0, shares[i] - shares[i - 1]));

                    // take average
                    var avg = tsRelative.Average() / 1000d;

                    // re-target if outside bounds
                    if (avg < tMin || avg > tMax)
                    {
                        var change = options.TargetTime / avg;
                        newDiff = difficulty * change;

                        // prevent huge jumps
                        var delta = newDiff.Value - ctx.Difficulty;
                        if (Math.Abs(delta) > maxJump)
                            newDiff = difficulty + (delta > 0 ? maxJump : -maxJump);

                        // round to next 100 if big enough
                        if (newDiff > 1000)
                            newDiff = Math.Round(newDiff.Value / 100d, 0) * 100;
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

                    // check if different
                    if (!newDiff.Value.EqualsDigitPrecision3(ctx.Difficulty))
                        ctx.VarDiff.LastUpdate = clock.Now;
                    else
                        newDiff = null;
                }

                return newDiff;
            }
        }
    }
}
