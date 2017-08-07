using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.Util;

namespace MiningCore.VarDiff
{
    public class VarDiffContext
    {
        public long LastTs { get; set; }
        public long LastRtc { get; set; }
        public CircularLongBuffer TimeBuffer { get; set; }
    }
}
