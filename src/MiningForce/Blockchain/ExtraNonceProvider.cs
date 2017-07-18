using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using MiningForce.Extensions;

namespace MiningForce.Blockchain
{
    public class ExtraNonceProvider
    {
        public ExtraNonceProvider()
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

        private uint counter;

        public int Size => 4;

        public uint Next()
        {
            return ++counter;
        }
    }
}
