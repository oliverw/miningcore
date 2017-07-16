using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Blockchain
{
    public class CommonJobContext
    {
        public double Difficulty { get; set; }
        public double PreviousDifficulty { get; set; }
        public string ExtraNonce1 { get; set; }
    }
}
