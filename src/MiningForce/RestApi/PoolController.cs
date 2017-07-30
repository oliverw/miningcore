using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MiningForce.Configuration;
using MiningForce.MininigPool;

namespace MiningForce.RestApi
{
	[Route("api/pool")]
    public class ClusterController : Controller
    {
	    public string[] Index()
	    {
		    return Program.Pools.Keys
			    .Select(x => x.Id)
			    .ToArray();
	    }

	    [Route("{poolId}/config")]
	    public PoolConfig GetPoolConfig(string poolId)
	    {
		    return Program.Pools.Keys.FirstOrDefault(x => x.Id == poolId);
	    }

	    [Route("{poolId}/stats")]
	    public object GetPoolStats(string poolId)
	    {
		    var poolConfig = Program.Pools.Keys.FirstOrDefault(x => x.Id == poolId);
		    if (poolConfig == null)
			    return null;

		    var pool = Program.Pools[poolConfig];

		    return new { pool = pool.PoolStats, network = pool.NetworkStats };
	    }
	}
}
