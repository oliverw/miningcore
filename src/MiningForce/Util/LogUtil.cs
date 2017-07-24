using System;
using System.Collections.Generic;
using System.Text;
using MiningForce.Configuration;
using NLog;

namespace MiningForce.Util
{
    public static class LogUtil
    {
	    public static ILogger GetPoolScopedLogger(Type type, PoolConfig poolConfig)
	    {
		    return LogManager.GetLogger(poolConfig.Id);
	    }
    }
}
