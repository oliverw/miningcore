namespace MiningCore
{
    public class Endpoint
    {
        public string Id { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
    }

    public class Configuration
    {
        public Endpoint[] Endpoints { get; set; }
    }
}
