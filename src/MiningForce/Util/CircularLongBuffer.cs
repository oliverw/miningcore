using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MiningForce.Util
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
