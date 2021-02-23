
using Miningcore.Configuration;

namespace Miningcore.Blockchain.Cryptonote.Configuration
{
    public class CryptonotePoolConfigExtra
    {
        /// <summary>
        /// Blocktemplate stream published via ZMQ
        /// </summary>
        public ZmqPubSubEndpointConfig BtStream { get; set; }
    }
}
