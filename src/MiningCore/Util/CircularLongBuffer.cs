using System.Linq;

namespace MiningCore.Util
{
    public class CircularLongBuffer : CircularBuffer<long>
    {
        public CircularLongBuffer(int capacity) : base(capacity)
        {
        }

        public CircularLongBuffer(int capacity, long[] items) : base(capacity, items)
        {
        }

        public double Average()
        {
            return ToArray().Average();
        }
    }
}
