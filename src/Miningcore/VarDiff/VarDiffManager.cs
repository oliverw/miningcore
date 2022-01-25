using CircularBuffer;
using Miningcore.Configuration;
using Miningcore.Time;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.VarDiff;

public class VarDiffManager
{
    public VarDiffManager(VarDiffConfig varDiffOptions, IMasterClock clock)
    {
        options = varDiffOptions;
        this.clock = clock;
        bufferSize = 10; // Last 10 shares is always enough

        var variance = varDiffOptions.TargetTime * (varDiffOptions.VariancePercent / 100.0);
        tMin = varDiffOptions.TargetTime - variance;
        tMax = varDiffOptions.TargetTime + variance;
    }

    private readonly int bufferSize;
    private readonly VarDiffConfig options;
    private readonly double tMax;
    private readonly double tMin;
    private readonly IMasterClock clock;

    public double? Update(VarDiffContext ctx, double difficulty, bool idle)
    {
        Contract.RequiresNonNull(ctx, nameof(ctx));

        var now = DateTimeOffset.Now;
        var ts = now.ToUnixTimeMilliseconds() / 1000.0;

        lock(ctx)
        {
            // For the first time, won't change diff.
            if(!ctx.LastTs.HasValue)
            {
                ctx.LastRetarget = ts;
                ctx.LastTs = ts;
                ctx.TimeBuffer = new CircularBuffer<double>(bufferSize);

                return null;
            }

            var minDiff = options.MinDiff;
            var maxDiff = options.MaxDiff ?? Math.Max(minDiff, double.MaxValue); // for regtest
            var timeDelta = ts - ctx.LastTs.Value;

            // Always calculate the time until now even there is no share submitted.
            var timeTotal = ctx.TimeBuffer.Sum() + timeDelta;
            var avg = timeTotal / (ctx.TimeBuffer.Size + 1);

            // Once there is a share submitted, store the time into the buffer and update the last time.
            if(!idle)
            {
                ctx.TimeBuffer.PushBack(timeDelta);
                ctx.LastTs = ts;
            }

            // Check if we need to change the difficulty
            if(ts - ctx.LastRetarget < options.RetargetTime || avg >= tMin && avg <= tMax)
                return null;

            // Possible New Diff
            var newDiff = difficulty * options.TargetTime / avg;

            // Max delta
            if(options.MaxDelta is > 0)
            {
                var delta = Math.Abs(newDiff - difficulty);

                if(delta > options.MaxDelta)
                {
                    if(newDiff > difficulty)
                        newDiff -= delta - options.MaxDelta.Value;
                    else if(newDiff < difficulty)
                        newDiff += delta - options.MaxDelta.Value;
                }
            }

            // Clamp to valid range
            if(newDiff < minDiff)
                newDiff = minDiff;
            if(newDiff > maxDiff)
                newDiff = maxDiff;

            // RTC if the Diff is changed
            if(newDiff < difficulty || newDiff > difficulty)
            {
                ctx.LastRetarget = ts;
                ctx.LastUpdate = clock.Now;

                // Due to change of diff, Buffer needs to be cleared
                ctx.TimeBuffer = new CircularBuffer<double>(bufferSize);

                return newDiff;
            }
        }

        return null;
    }
}
