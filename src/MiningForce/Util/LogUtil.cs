using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;
using MiningForce.MininigPool;
using NLog;

namespace MiningForce.Util
{
    public static class LogUtil
    {
	    public static ILogger GetPoolScopedLogger(Type type, PoolConfig poolConfig)
	    {
		    return LogManager.GetLogger(poolConfig.Id);
	    }

	    public static void ThrowLogPoolStartupException(this ILogger logger, string msg, string category = null)
	    {
		    var output = !string.IsNullOrEmpty(category) ? $"[{category}] {msg}" : msg;
		    logger.Error(output);

			throw new PoolStartupAbortException(msg);
		}

	    public static void ThrowLogPoolStartupException(this ILogger logger, Exception ex, string msg, string category = null)
	    {
		    var output = !string.IsNullOrEmpty(category) ? $"[{category}] {msg}" : msg;
			logger.Error(ex, output);

		    throw new PoolStartupAbortException(msg);
	    }
	}
}
