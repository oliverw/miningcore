using System;
using NLog;

namespace MiningForce.Configuration.Extensions
{
    public static class LoggingExtensions
    {
	    public static void Trace(this ILogger logger, Func<string> output)
	    {
		    if (logger.IsEnabled(LogLevel.Trace))
			    logger.Trace(output());
	    }

        public static void Debug(this ILogger logger, Func<string> output)
        {
			if (logger.IsEnabled(LogLevel.Debug))
                logger.Debug(output());
        }

        public static void Info(this ILogger logger, Func<string> output)
        {
            if(logger.IsEnabled(LogLevel.Info))
                logger.Info(output());
        }

        public static void Warning(this ILogger logger, Func<string> output)
        {
            if (logger.IsEnabled(LogLevel.Warn))
                logger.Warn(output());
        }

        public static void Error(this ILogger logger, Func<string> output, Exception ex = null)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                if(ex == null)
                    logger.Error(output());
                else
                    logger.Error(ex, output());
            }
        }

        public static void Error(this ILogger logger, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.Error(ex, String.Empty);
            }
        }

        public static void Critical(this ILogger logger, Func<string> output)
        {
            if (logger.IsEnabled(LogLevel.Fatal))
                logger.Fatal(output());
        }
    }
}
