using System.Threading.Tasks;

namespace MiningCore.Notifications
{
    public interface INotificationSender
    {
        Task NotifyAsync(string recipient, string subject, string body);
    }

    public enum NotificationType
    {
        Email,
        Sms
    }
}
