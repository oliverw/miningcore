using Microsoft.AspNetCore.Mvc;
using MiningForce.Configuration;

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
