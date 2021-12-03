using System.Diagnostics;

namespace Miningcore.Util;

public static class TimingUtils
{
    public static async Task Timed(Func<Task> action, Action<TimeSpan> elapsedHandler)
    {
        var sw = Stopwatch.StartNew();

        await action();

        elapsedHandler(sw.Elapsed);
    }

    public static async Task<T> Timed<T>(Func<Task<T>> action, Action<TimeSpan> elapsedHandler)
    {
        var sw = Stopwatch.StartNew();

        var result = await action();
        elapsedHandler(sw.Elapsed);

        return result;
    }

    public static async Task<T> Timed<T>(Func<Task<T>> action, Action<T, TimeSpan> elapsedHandler)
    {
        var sw = Stopwatch.StartNew();

        var result = await action();
        elapsedHandler(result, sw.Elapsed);

        return result;
    }

    public static void Timed(Action action, Action<TimeSpan> elapsedHandler)
    {
        var sw = Stopwatch.StartNew();

        action();

        elapsedHandler(sw.Elapsed);
    }

    public static T Timed<T>(Func<T> action, Action<TimeSpan> elapsedHandler)
    {
        var sw = Stopwatch.StartNew();

        var result = action();
        elapsedHandler(sw.Elapsed);

        return result;
    }
}
