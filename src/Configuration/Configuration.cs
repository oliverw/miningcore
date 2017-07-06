namespace MiningCore.Configuration
{
    public enum MiningAlgorithm
    {
        Sha256 = 1,
    }

    public class Coin
    {
        public string Name { get; set; }
        public int Symbol { get; set; }
        public MiningAlgorithm Algorithm { get; set; }
    }

    public class Endpoint
    {
        public string Address { get; set; }
        public int Port { get; set; }
    }

    public class Daemon : Endpoint
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Protocol { get; set; }
    }

    public class Pool
    {
        public Coin Coin { get; set; }
        public Endpoint StratumEndpoint { get; set; }
        public Daemon[] Daemons { get; set; }
    }

    public class Configuration
    {
        public Pool[] Pools { get; set; }
    }
}
