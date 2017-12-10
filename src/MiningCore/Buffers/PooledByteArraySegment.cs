using System;
using System.Buffers;

namespace MiningCore.Buffers
{
    public struct PooledArraySegment<T> : IDisposable
    {
        public PooledArraySegment(T[] array, int offset, int size)
        {
            Array = array;
            Size = size;
            Offset = offset;
        }

        public PooledArraySegment(int size, int offset = 0)
        {
            Array = ArrayPool<T>.Shared.Rent(size);
            Size = size;
            Offset = offset;
        }

        public T[] Array { get; private set; }
        public int Size { get; }
        public int Offset { get; }

        public T[] ToArray()
        {
            var result = new T[Size];
            System.Array.Copy(Array, Offset, result, 0, Size);
            return result;
        }

        #region IDisposable

        public void Dispose()
        {
            var array = Array;

            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array);
                Array = null;
            }
        }

        #endregion
    }
}
