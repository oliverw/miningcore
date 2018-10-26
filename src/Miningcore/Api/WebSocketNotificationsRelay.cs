using System;
using Autofac;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using WebSocketManager;

namespace Miningcore.Api
{
    public class WebSocketNotificationsRelay : WebSocketHandler
    {
        public WebSocketNotificationsRelay(WebSocketConnectionManager webSocketConnectionManager, IComponentContext ctx) : 
            base(webSocketConnectionManager)
        {
            messageBus = ctx.Resolve<IMessageBus>();

            messageBus.Listen<BlockNotification>().Subscribe(OnBlockNotification);
        }

        private IMessageBus messageBus;

        private void OnBlockNotification(BlockNotification notification)
        {
        }
    }
}
