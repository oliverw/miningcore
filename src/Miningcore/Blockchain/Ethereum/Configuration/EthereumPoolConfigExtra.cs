using Miningcore.Configuration;

namespace Miningcore.Blockchain.Ethereum.Configuration
{
    public class EthereumPoolConfigExtra
    {
        /// <summary>
        /// Base directory for generated DAGs
        /// </summary>
        public string DagDir { get; set; }

        /// <summary>
        /// If true connects to Websocket port of all daemons and subscribes to streaming job updates for reduced latency
        /// </summary>
        public bool? EnableDaemonWebsocketStreaming { get; set; }

        /// <summary>
        /// Useful to specify the real chain type when running geth
        /// </summary>
        public string ChainTypeOverride { get; set; }

        /// <summary>
        /// getWork stream published via ZMQ
        /// </summary>
        public ZmqPubSubEndpointConfig BtStream { get; set; }
    }
}
