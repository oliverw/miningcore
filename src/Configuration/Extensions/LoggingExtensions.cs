using System;
using Microsoft.Extensions.Logging;

namespace MiningCore.Extensions
{
    public static class LoggingExtensions
    {
        public static void Debug(this ILogger logger, Func<string> output)
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug(output());
        }

        public static void Info(this ILogger logger, Func<string> output)
        {
            if(logger.IsEnabled(LogLevel.Information))
                logger.LogInformation(output());
        }

        public static void Warning(this ILogger logger, Func<string> output)
        {
            if (logger.IsEnabled(LogLevel.Warning))
                logger.LogWarning(output());
        }

        public static void Error(this ILogger logger, Func<string> output, Exception ex = null)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                if(ex == null)
                    logger.LogError(output());
                else
                    logger.LogError(default(EventId), ex, output());
            }
        }

        public static void Error(this ILogger logger, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(default(EventId), ex, String.Empty);
            }
        }

        public static void Critical(this ILogger logger, Func<string> output)
        {
            if (logger.IsEnabled(LogLevel.Critical))
                logger.LogCritical(output());
        }
    }
}
