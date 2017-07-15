using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Blockchain.Bitcoin.Messages
{
    public class GetInfoResponse
    {
        public int Version { get; set; }
        public int ProtocolVersion { get; set; }
        public double Balance { get; set; }
        todo rest
    }
}
