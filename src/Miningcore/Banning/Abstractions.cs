using System.Net;

namespace Miningcore.Banning;

public interface IBanManager
{
    bool IsBanned(IPAddress address);
    void Ban(IPAddress address, TimeSpan duration);
}
