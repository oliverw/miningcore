namespace MiningCore.Blockchain.Ethereum.DaemonResponses
{
    public class PubSubParams<T>
    {
        public string Subscription { get; set; }
        public T Result { get; set; }
    }
}
