using System.Net;
using Miningcore.Configuration;

namespace Miningcore.Stratum;

public record StratumEndpoint(IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint);
