using Miningcore.Mining;
using Miningcore.Persistence.Model;

namespace Miningcore.Notifications.Messages
{
    public enum PoolStatus
    {
        Online,
        Offline
    }

    public class PoolStatusNotification
    {
        public IMiningPool Pool { get; set; }
        public PoolStatus Status { get; set; }
    }
}
