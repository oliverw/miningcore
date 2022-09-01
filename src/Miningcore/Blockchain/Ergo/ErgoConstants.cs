using System.Text.RegularExpressions;

// ReSharper disable InconsistentNaming

namespace Miningcore.Blockchain.Ergo;

public static class ErgoConstants
{
    public const uint ShareMultiplier = 256;
    public const decimal SmallestUnit = 1000000000;
    public static readonly Regex RegexChain = new("ergo-([^-]+)-.+", RegexOptions.Compiled);

    public static readonly byte[] M = Enumerable.Range(0, 1024)
        .Select(x => BitConverter.GetBytes((ulong) x).Reverse())
        .SelectMany(byteArr => byteArr)
        .ToArray();
}
