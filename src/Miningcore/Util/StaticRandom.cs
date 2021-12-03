namespace Miningcore.Util;

public static class StaticRandom
{
    static int seed = Environment.TickCount;

    private static readonly ThreadLocal<Random> random =
        new(() => new Random(Interlocked.Increment(ref seed)));

    public static int Next()
    {
        return random.Value.Next();
    }

    public static int Next(int n)
    {
        return random.Value.Next(n);
    }
}
