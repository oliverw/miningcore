using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Miningcore.Util
{
    public static class TimingUtils
    {
        public static async Task Timed(Func<Task> action, Action<TimeSpan> elapsedHandler)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                await action();
            }

            finally
            {
                sw.Stop();
            }

            elapsedHandler(sw.Elapsed);
        }

        public static async Task<T> Timed<T>(Func<Task<T>> action, Action<TimeSpan> elapsedHandler)
        {
            T result;

            var sw = Stopwatch.StartNew();
            sw.Stop();

            try
            {
                result = await action();
            }

            finally
            {
                sw.Stop();
            }

            elapsedHandler(sw.Elapsed);
            return result;
        }

        public static async Task<T> Timed<T>(Func<Task<T>> action, Action<T, TimeSpan> elapsedHandler)
        {
            T result;

            var sw = Stopwatch.StartNew();
            sw.Stop();

            try
            {
                result = await action();
            }

            finally
            {
                sw.Stop();
            }

            elapsedHandler(result, sw.Elapsed);
            return result;
        }

        public static void Timed(Action action, Action<TimeSpan> elapsedHandler)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                action();
            }

            finally
            {
                sw.Stop();
            }

            elapsedHandler(sw.Elapsed);
        }

        public static T Timed<T>(Func<T> action, Action<TimeSpan> elapsedHandler)
        {
            T result;

            var sw = Stopwatch.StartNew();
            sw.Stop();

            try
            {
                result = action();
            }

            finally
            {
                sw.Stop();
            }

            elapsedHandler(sw.Elapsed);

            return result;
        }
    }
}
