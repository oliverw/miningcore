using System;
using System.Buffers;
using MiningCore.Extensions;
using NBitcoin.BouncyCastle.Math;

namespace MiningCore.Blockchain.ZCash
{
    public static class ZCashUtils
    {
        public static unsafe string EncodeTarget(double difficulty, ZCashChainConfig chainConfig)
        {
            string result;
            var diff = BigInteger.ValueOf((long)(difficulty * 255d));
            var quotient = chainConfig.Diff1.Divide(diff).Multiply(BigInteger.ValueOf(255));
            var bytes = quotient.ToByteArray().AsSpan();
            Span<byte> padded = stackalloc byte[ZCashConstants.TargetPaddingLength];

            var padLength = ZCashConstants.TargetPaddingLength - bytes.Length;

            if (padLength > 0)
            {
                bytes.CopyTo(padded.Slice(padLength, bytes.Length));
                result = padded.ToHexString(0, ZCashConstants.TargetPaddingLength);
            }

            else
                result = bytes.ToHexString(0, ZCashConstants.TargetPaddingLength);

            return result;
        }
    }
}
