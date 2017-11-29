using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Blockchain.Ethereum.Configuration
{
    public class EthereumDaemonEndpointConfigExtra
    {
        /// <summary>
        /// Optional port for streaming WebSocket data
        /// </summary>
        public int? PortWs { get; set; }
    }
}
