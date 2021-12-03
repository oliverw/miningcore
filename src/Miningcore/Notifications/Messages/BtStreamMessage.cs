namespace Miningcore.Notifications.Messages;

public record BtStreamMessage
{
    public BtStreamMessage(string topic, string payload, DateTime sent, DateTime received)
    {
        Topic = topic;
        Payload = payload;
        Sent = sent;
        Received = received;
    }

    public string Topic { get; }
    public string Payload { get; }
    public DateTime Sent { get; }
    public DateTime Received { get; }
}
