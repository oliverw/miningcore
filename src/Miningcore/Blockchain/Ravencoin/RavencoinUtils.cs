using Miningcore.Extensions;
using Org.BouncyCastle.Math;

namespace Miningcore.Blockchain.Ravencoin;

public static class RavencoinUtils
{
    public static string EncodeTarget(double difficulty)
    {
        string result;
        var diff = BigInteger.ValueOf((long) (difficulty * 255d));
        var quotient = RavencoinConstants.Diff1B.Divide(diff).Multiply(BigInteger.ValueOf(255));
        var bytes = quotient.ToByteArray().AsSpan();
        Span<byte> padded = stackalloc byte[RavencoinConstants.TargetPaddingLength];

        var padLength = RavencoinConstants.TargetPaddingLength - bytes.Length;

        if(padLength > 0)
        {
            bytes.CopyTo(padded.Slice(padLength, bytes.Length));
            result = padded.ToHexString(0, RavencoinConstants.TargetPaddingLength);
        }

        else
            result = bytes.ToHexString(0, RavencoinConstants.TargetPaddingLength);

        return result;
    }
}