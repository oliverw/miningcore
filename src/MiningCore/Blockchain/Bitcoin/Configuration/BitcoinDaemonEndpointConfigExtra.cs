namespace MiningCore.Blockchain.Bitcoin.Configuration
{
    public class BitcoinDaemonEndpointConfigExtra
    {
        /// <summary>
        /// Optional port for streaming block updates via ZMQ
        /// </summary>
        public int? PortZmqBlocks { get; set; }

        /// <summary>
        /// Optional port for streaming block updates via ZMQ
        /// </summary>
        public int? PortZmqTx { get; set; }
    }
}
