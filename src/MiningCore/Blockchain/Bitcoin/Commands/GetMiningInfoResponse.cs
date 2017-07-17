using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Blockchain.Bitcoin.Commands
{
    public class GetMiningInfoResponse
    {
        public int Blocks { get; set; }
        public int CurrentBlockSize { get; set; }
        public int CurrentBlockWeight { get; set; }
        public double Difficulty { get; set; }
        public double NetworkHashps { get; set; }
        public string Chain { get; set; }
    }
}
