using System;
using System.Buffers;

namespace MiningCore.Buffers
{
    public class PooledArraySegment<T> : IDisposable
    {
        public PooledArraySegment(T[] array, int offset, int size)
        {
            Array = array;
            Size = size;
            Offset = offset;
        }

        public PooledArraySegment(int offset, int size)
        {
            Array = ArrayPool<T>.Shared.Rent(size);
            Size = size;
            Offset = offset;
        }

        public T[] Array { get; private set; }
        public int Size { get; }
        public int Offset { get; }

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            if (Array != null)
            {
                ArrayPool<T>.Shared.Return(Array);
                Array = null;
            }
        }

        #endregion
    }
}
