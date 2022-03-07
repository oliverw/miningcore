using Miningcore.Contracts;
using Miningcore.Native;
using Miningcore.Time;

namespace Miningcore.Crypto.Hashing.Algorithms;

[Identifier("scryptn")]
public unsafe class ScryptN : IHashAlgorithm
{
    public ScryptN(Tuple<long, long>[] timetable = null)
    {
        this.timetable = timetable ?? defaultTimetable;
    }

    private readonly Tuple<long, long>[] timetable;

    public IMasterClock Clock { get; set; }

    private static readonly Tuple<long, long>[] defaultTimetable = new[]
    {
        Tuple.Create(2048L, 1389306217L),
        Tuple.Create(4096L, 1456415081L),
        Tuple.Create(8192L, 1506746729L),
        Tuple.Create(16384L, 1557078377L),
        Tuple.Create(32768L, 1657741673L),
        Tuple.Create(65536L, 1859068265L),
        Tuple.Create(131072L, 2060394857L),
        Tuple.Create(262144L, 1722307603L),
        Tuple.Create(524288L, 1769642992L),
    }.OrderByDescending(x => x.Item1).ToArray();

    public void Digest(ReadOnlySpan<byte> data, Span<byte> result, params object[] extra)
    {
        Contract.Requires<ArgumentException>(result.Length >= 32);

        // get nFactor
        var ts = ((DateTimeOffset) Clock.Now).ToUnixTimeSeconds();
        var n = timetable.First(x => ts >= x.Item2).Item1;
        var nFactor = Math.Log(n) / Math.Log(2);

        fixed (byte* input = data)
        {
            fixed (byte* output = result)
            {
                Multihash.scryptn(input, output, (uint) nFactor, (uint) data.Length);
            }
        }
    }
}
