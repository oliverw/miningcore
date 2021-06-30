using System;
using Miningcore.Configuration;
using Miningcore.Util;

namespace Miningcore.VarDiff
{
    public class VarDiffContext
    {
        public double? LastTs { get; set; }
        public double LastRtc { get; set; }
        public CircularDoubleBuffer TimeBuffer { get; set; }
        public DateTime? LastUpdate { get; set; }
        public VarDiffConfig Config { get; set; }
    }
}
