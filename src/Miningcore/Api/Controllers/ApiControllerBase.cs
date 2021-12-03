using System.Net;
using Autofac;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Configuration;
using Miningcore.Persistence;

namespace Miningcore.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ApiControllerBase(IComponentContext ctx)
    {
        mapper = ctx.Resolve<IMapper>();
        clusterConfig = ctx.Resolve<ClusterConfig>();
        cf = ctx.Resolve<IConnectionFactory>();
    }

    protected readonly ClusterConfig clusterConfig;
    protected readonly IConnectionFactory cf;
    protected readonly IMapper mapper;

    protected PoolConfig GetPoolNoThrow(string poolId)
    {
        if(string.IsNullOrEmpty(poolId))
            return null;

        var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);
        return pool;
    }

    protected PoolConfig GetPool(string poolId)
    {
        if(string.IsNullOrEmpty(poolId))
            throw new ApiException("Invalid pool id", HttpStatusCode.NotFound);

        var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

        if(pool == null)
            throw new ApiException($"Unknown pool {poolId}", HttpStatusCode.NotFound);

        return pool;
    }
}
