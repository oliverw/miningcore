using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NLog;

namespace MiningCore.Extensions
{
    public static class LoggingExtensions
    {
        public static void LogInvoke(this ILogger logger, object[] args = null, [CallerMemberName] string caller = null)
        {
            if(args == null)
                logger.Debug(() => $"{caller}()");
            else
                logger.Debug(()=> $"{caller}({string.Join(", ", args.Select(x=> x?.ToString()))})");
        }

        public static void LogInvoke(this ILogger logger, string logCat, object[] args = null, [CallerMemberName] string caller = null)
        {
            if (args == null)
                logger.Debug(() => $"[{logCat}] {caller}()");
            else
                logger.Debug(() => $"[{logCat}] {caller}({string.Join(", ", args.Select(x => x?.ToString()))})");
        }
    }
}
