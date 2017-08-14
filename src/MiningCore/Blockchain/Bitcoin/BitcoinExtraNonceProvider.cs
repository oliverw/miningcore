using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MiningCore.Extensions;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinExtraNonceProvider
    {
        private uint counter;

        public BitcoinExtraNonceProvider()
        {
            uint instanceId;

            using (var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                rng.GetNonZeroBytes(bytes);
                instanceId = BitConverter.ToUInt32(bytes, 0);
            }

            counter = instanceId << 27;
        }

        public byte[] PlaceHolder { get; } = "f000000ff111111f".HexToByteArray();

        public int Size => PlaceHolder.Length - Marshal.SizeOf(counter);

        public uint Next()
        {
            return ++counter;
        }
    }
}
