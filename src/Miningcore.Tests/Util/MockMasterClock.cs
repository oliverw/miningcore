using System;
using Miningcore.Time;

namespace Miningcore.Tests.Util;

public class MockMasterClock : IMasterClock
{
    public DateTime CurrentTime { get; set; }

    public DateTime Now => CurrentTime;
}
