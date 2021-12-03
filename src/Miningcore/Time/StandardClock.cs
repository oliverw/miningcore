namespace Miningcore.Time;

public class StandardClock : IMasterClock
{
    public DateTime Now => DateTime.UtcNow;
}
