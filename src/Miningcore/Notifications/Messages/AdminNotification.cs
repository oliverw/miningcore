namespace Miningcore.Notifications.Messages;

public record AdminNotification
{
    public AdminNotification(string subject, string message)
    {
        Subject = subject;
        Message = message;
    }

    public string Subject { get; }
    public string Message { get; }
}
