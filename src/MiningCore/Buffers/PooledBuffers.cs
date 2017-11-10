using System.Buffers;

namespace MiningCore.Buffers
{
    public static class PooledBuffers
    {
        public static ArrayPool<byte> Byte = ArrayPool<byte>.Shared;
    }
}
