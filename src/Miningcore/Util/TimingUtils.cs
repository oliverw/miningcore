using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Miningcore.Util
{
    public static class TimingUtils
    {
        public static async Task Timed(Func<Task> action, Action<TimeSpan> elapsedHandler)
        {
            var sw = new Stopwatch();

            sw.Start();
            await action();
            sw.Stop();

            elapsedHandler(sw.Elapsed);
        }

        public static async Task<T> Timed<T>(Func<Task<T>> action, Action<TimeSpan> elapsedHandler)
        {
            var sw = new Stopwatch();

            sw.Start();
            var result = await action();
            sw.Stop();

            elapsedHandler(sw.Elapsed);
            return result;
        }

        public static async Task<T> Timed<T>(Func<Task<T>> action, Action<T, TimeSpan> elapsedHandler)
        {
            var sw = new Stopwatch();

            sw.Start();
            var result = await action();
            sw.Stop();

            elapsedHandler(result, sw.Elapsed);
            return result;
        }
    }
}
