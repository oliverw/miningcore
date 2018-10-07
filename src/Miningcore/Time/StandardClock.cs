using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Time
{
    public class StandardClock : IMasterClock
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
