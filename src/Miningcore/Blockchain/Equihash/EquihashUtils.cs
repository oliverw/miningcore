using Miningcore.Configuration;
using Miningcore.Extensions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Equihash;

public static class EquihashUtils
{
    public static string EncodeTarget(double difficulty, EquihashCoinTemplate.EquihashNetworkParams chainConfig)
    {
        string result;
        var diff = BigInteger.ValueOf((long) (difficulty * 255d));
        var quotient = chainConfig.Diff1Value.Divide(diff).Multiply(BigInteger.ValueOf(255));
        var bytes = quotient.ToByteArray().AsSpan();
        Span<byte> padded = stackalloc byte[EquihashConstants.TargetPaddingLength];

        var padLength = EquihashConstants.TargetPaddingLength - bytes.Length;

        if(padLength > 0)
        {
            bytes.CopyTo(padded.Slice(padLength, bytes.Length));
            result = padded.ToHexString(0, EquihashConstants.TargetPaddingLength);
        }

        else
            result = bytes.ToHexString(0, EquihashConstants.TargetPaddingLength);

        return result;
    }
}
