namespace MiningCore.Configuration
{
    public enum HashAlgorithm
    {
        Sha256 = 1,
    }

    public class Coin
    {
        public string Name { get; set; }
        public int Symbol { get; set; }
        public HashAlgorithm Algorithm { get; set; }
    }

    public class NetworkEndpoint
    {
        public string Address { get; set; }
        public int Port { get; set; }
    }

    public class AuthenticatedNetworkEndpoint : NetworkEndpoint
    {
        public string Username { get; set; }
        public int Password { get; set; }
    }

    public class DatabaseEndpoint : AuthenticatedNetworkEndpoint
    {
        public string Database { get; set; }
    }

    public class Pool
    {
        public Coin Coin { get; set; }
        public NetworkEndpoint[] Ports { get; set; }
        public AuthenticatedNetworkEndpoint[] Daemons { get; set; }
    }

    public class ClusterConfiguration
    {
        public Pool[] Pools { get; set; }
    }
}
