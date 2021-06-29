using System.Numerics;
using Miningcore.Serialization;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Ethereum.DaemonResponses
{
    public class Web3Version
    {
        public string Api { get; set; }
        public uint Ethereum { get; set; }
        public uint Network { get; set; }
        public string Node { get; set; }
    }
}
