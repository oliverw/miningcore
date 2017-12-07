using System;
using MiningCore.Time;

namespace MiningCore.Tests.Util
{
    public class MockMasterClock : IMasterClock
    {
        public DateTime CurrentTime { get; set; }

        public DateTime Now => CurrentTime;
    }
}
