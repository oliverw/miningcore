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
	}
}
