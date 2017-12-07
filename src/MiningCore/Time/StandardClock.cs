using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Time
{
    public class StandardClock : IMasterClock
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
