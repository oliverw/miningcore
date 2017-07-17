using System;
using System.Collections.Generic;
using System.Text;

namespace MiningForce.Blockchain.Bitcoin.Commands
{
    public class GetInfoResponse
    {
        public int Version { get; set; }
        public int ProtocolVersion { get; set; }
        public double Balance { get; set; }
        public int Blocks { get; set; }
        public int Timeoffset { get; set; }
        public int Connections { get; set; }
        public double Difficulty { get; set; }
        public bool Testnet { get; set; }
        public double KeypoolOldest { get; set; }
        public int KeypoolSize { get; set; }
        public double PaytxFee { get; set; }
        public double RelayFee { get; set; }
        public string Errors { get; set; }
    }
}
