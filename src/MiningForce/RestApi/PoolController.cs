using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MiningForce.Configuration;
using MiningForce.MininigPool;

namespace MiningForce.RestApi
{
	[Route("api")]
    public class PoolController : Controller
    {
	    [Route("config")]
	    public ClusterConfig GetConfig()
	    {
		    return Program.ClusterConfig;
	    }

	    [Route("pool")]
	    public string[] GetPools()
	    {
		    return Program.Pools.Keys
			    .Select(x => x.Id)
			    .ToArray();
	    }

	    [Route("pool/{poolId}/config")]
	    public PoolConfig GetPoolConfig(string poolId)
	    {
		    return Program.Pools.Keys.FirstOrDefault(x => x.Id == poolId);
	    }

	    [Route("pool/{poolId}/stats")]
	    public dynamic GetPoolStats(string poolId)
	    {
		    var poolConfig = Program.Pools.Keys.FirstOrDefault(x => x.Id == poolId);
		    if (poolConfig == null)
			    return null;

		    var pool = Program.Pools[poolConfig];

			return new { pool = pool.PoolStats, network = pool.NetworkStats };
	    }
	}
}
