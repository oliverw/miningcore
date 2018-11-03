namespace Miningcore.Notifications.Messages
{
    public class BtStreamMessage
    {
        public BtStreamMessage(string topic, string payload)
        {
            Topic = topic;
            Payload = payload;
        }

        public string Topic { get; }
        public string Payload { get; }
    }
}
