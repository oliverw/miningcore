using Miningcore.Configuration;
using Miningcore.Mining;
using NLog;

namespace Miningcore.Util;

public static class LogUtil
{
    public static ILogger GetPoolScopedLogger(Type type, PoolConfig poolConfig)
    {
        return LogManager.GetLogger(poolConfig.Id);
    }

    public static ILogger GetPoolScopedLogger(Type type, string poolId)
    {
        return LogManager.GetLogger(poolId);
    }
}
