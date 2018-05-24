using Easy.MessageHub;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventHandler
{
    public class SocketEventHandler
    {
        MessageHub _messageHub;

        public SocketEventHandler()
        {
            Console.WriteLine(">>>>>Socket event handler has been initiated");
            _messageHub = MessageHub.Instance;

        }

        public Guid Subscribe<T>(Action<T> action)
        {
            return _messageHub.Subscribe(action);
        }

        public void Publish<T>(T message)
        {
            _messageHub.Publish<T>(message);
        }

        public void ClearAllSubscriptions()
        {
            _messageHub.ClearSubscriptions();
        }

        public void UnSubscribeSubscription(Guid subscrioptionToken)
        {
            _messageHub.UnSubscribe(subscrioptionToken);
        }

        public bool IsSubscribed(Guid guid)
        {
            return _messageHub.IsSubscribed(guid);
        }

    }
}
