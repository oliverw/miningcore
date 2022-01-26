using CircularBuffer;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Time;

namespace Miningcore.VarDiff;

public class VarDiffManager
{
    private const int BufferSize = 10;  // Last 10 shares is always enough

    public double? Update(VarDiffContext ctx, VarDiffConfig options, IMasterClock clock, double difficulty)
    {
        var now = clock.Now;
        var ts = now.ToUnixSeconds();

        lock(ctx)
        {
            if(ctx.LastTs.HasValue)
            {
                var minDiff = options.MinDiff;
                var maxDiff = options.MaxDiff ?? Math.Max(minDiff, double.MaxValue); // for regtest
                var timeDelta = ts - ctx.LastTs.Value;

                // make sure buffer exists as this point
                ctx.TimeBuffer ??= new CircularBuffer<double>(BufferSize);

                // Always calculate the time until now even there is no share submitted.
                var timeTotal = ctx.TimeBuffer.Sum() + timeDelta;
                var avg = timeTotal / (ctx.TimeBuffer.Size + 1);

                // Once there is a share submitted, store the time into the buffer and update the last time.
                ctx.TimeBuffer.PushBack(timeDelta);
                ctx.LastTs = ts;

                // Check if we need to change the difficulty
                var variance = options.TargetTime * (options.VariancePercent / 100.0);
                var tMin = options.TargetTime - variance;
                var tMax = options.TargetTime + variance;

                if(ts - ctx.LastRetarget < options.RetargetTime || avg >= tMin && avg <= tMax)
                    return null;

                // Possible New Diff
                var newDiff = difficulty * options.TargetTime / avg;

                if(TryApplyNewDiff(ref newDiff, difficulty, minDiff, maxDiff, ts, ctx, options, clock))
                    return newDiff;
            }

            else
            {
                // init
                ctx.LastRetarget = ts;
                ctx.LastTs = ts;
            }
        }

        return null;
    }

    const double SafetyMargin = 1;    // ensure we don't miss a cycle due a sub-second fraction delta;

    public double? IdleUpdate(VarDiffContext ctx, VarDiffConfig options, IMasterClock clock, double difficulty)
    {
        var now = clock.Now;
        var ts = now.ToUnixSeconds();

        lock(ctx)
        {
            double timeDelta;

            if(ctx.LastTs.HasValue)
                timeDelta = ts - ctx.LastTs.Value;
            else
                timeDelta = ts - ctx.Created.ToUnixSeconds();

            timeDelta += SafetyMargin;

            // we only get involved if there was never an update or the last update happened longer than retargetTime ago
            if(timeDelta < options.RetargetTime)
                return null;

            // update the last time
            ctx.LastTs = ts;

            var minDiff = options.MinDiff;
            var maxDiff = options.MaxDiff ?? Math.Max(minDiff, double.MaxValue); // for regtest

            // Always calculate the time until now even there is no share submitted.
            var timeTotal = (ctx.TimeBuffer?.Sum() ?? 0) + (timeDelta - SafetyMargin);
            var avg = timeTotal / ((ctx.TimeBuffer?.Size ?? 0) + 1);

            // Possible New Diff
            var newDiff = difficulty * options.TargetTime / avg;

            if(TryApplyNewDiff(ref newDiff, difficulty, minDiff, maxDiff, ts, ctx, options, clock))
                return newDiff;
        }

        return null;
    }

    private bool TryApplyNewDiff(ref double newDiff, double oldDiff, double minDiff, double maxDiff, double ts,
        VarDiffContext ctx, VarDiffConfig options, IMasterClock clock)
    {
        // Max delta
        if(options.MaxDelta is > 0)
        {
            var delta = Math.Abs(newDiff - oldDiff);

            if(delta > options.MaxDelta)
            {
                if(newDiff > oldDiff)
                    newDiff -= delta - options.MaxDelta.Value;
                else if(newDiff < oldDiff)
                    newDiff += delta - options.MaxDelta.Value;
            }
        }

        // Clamp to valid range
        if(newDiff < minDiff)
            newDiff = minDiff;
        if(newDiff > maxDiff)
            newDiff = maxDiff;

        // RTC if the Diff is changed
        if(!(newDiff < oldDiff) && !(newDiff > oldDiff))
            return false;

        ctx.LastRetarget = ts;
        ctx.LastUpdate = clock.Now;

        // Due to change of diff, Buffer needs to be cleared
        if(ctx.TimeBuffer != null)
            ctx.TimeBuffer = new CircularBuffer<double>(BufferSize);

        return true;
    }
}
