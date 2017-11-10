using System.Buffers;
using Microsoft.IO;

namespace MiningCore.Buffers
{
    public static class PooledBuffers
    {
        public static ArrayPool<byte> Bytes = ArrayPool<byte>.Shared;
        public static ArrayPool<char> Chars = ArrayPool<char>.Shared;

        private static readonly RecyclableMemoryStreamManager recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();

        public static RecyclableMemoryStream GetRecyclableMemoryStream(int? requiredSize = null, string tag = null)
        {
            if(!requiredSize.HasValue)
                return (RecyclableMemoryStream)recyclableMemoryStreamManager.GetStream();

            return (RecyclableMemoryStream) recyclableMemoryStreamManager.GetStream(tag ?? string.Empty, requiredSize.Value);
        }
    }
}
