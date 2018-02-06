using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Buffers;

namespace MiningCore.Extensions
{
    public static class PoolingExtensions
    {
        public static void Dispose<T>(this IEnumerable<PooledArraySegment<T>> col)
        {
            foreach(var seg in col)
                seg.Dispose();
        }
    }
}
