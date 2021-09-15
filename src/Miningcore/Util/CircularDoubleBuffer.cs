using System.Linq;

namespace Miningcore.Util
{
    public class CircularDoubleBuffer : CircularBuffer<double>
    {
        public CircularDoubleBuffer(int capacity) : base(capacity)
        {
        }

        public CircularDoubleBuffer(int capacity, double[] items) : base(capacity, items)
        {
        }

        public double Average()
        {
            return ToArray().Average();
        }

        public double Sum()
        {
            double sum = 0;
            using(var enumerator = GetEnumerator())
            {
                while(enumerator.MoveNext())
                {
                    sum += enumerator.Current;
                }
            }

            return sum;
        }
    }
}
