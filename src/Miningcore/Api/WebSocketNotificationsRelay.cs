using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using WebSocketManager;
using WebSocketManager.Common;

namespace Miningcore.Api
{
    public class WebSocketNotificationsRelay : WebSocketHandler
    {
        public WebSocketNotificationsRelay(WebSocketConnectionManager webSocketConnectionManager, IComponentContext ctx) : 
            base(webSocketConnectionManager, new StringMethodInvocationStrategy())
        {
            messageBus = ctx.Resolve<IMessageBus>();

            serializer = new JsonSerializer
            {
                ContractResolver = ctx.Resolve<JsonSerializerSettings>().ContractResolver
            };

            Relay<BlockFoundNotification>(NotificationType.BlockFound);
            Relay<BlockUnlockedNotification>(NotificationType.BlockUnlocked);
            Relay<BlockConfirmationProgressNotification>(NotificationType.BlockUnlockProgress);
            Relay<NewChainHeightNotification>(NotificationType.NewChainHeight);
            Relay<PaymentNotification>(NotificationType.Payment);
        }

        private IMessageBus messageBus;
        private JsonSerializer serializer;
        private static ILogger logger = LogManager.GetCurrentClassLogger();

        public override async Task OnConnected(WebSocket socket)
        {
            WebSocketConnectionManager.AddSocket(socket);

            var greeting = ToJson(NotificationType.Greeting, new { Message = "Connected to Miningcore notification relay" });
            await socket.SendAsync(greeting, CancellationToken.None);
        }

        private void Relay<T>(NotificationType type)
        {
            messageBus.Listen<T>()
                .Select(x => Observable.FromAsync(() => BroadcastNotification(type, x)))
                .Concat()
                .Subscribe();
        }

        private async Task BroadcastNotification<T>(NotificationType type, T notification)
        {
            try
            {
                var json = ToJson(type, notification);

                var msg = new Message
                {
                    MessageType = MessageType.TextRaw,
                    Data = json
                };

                await SendMessageToAllAsync(msg);
            }

            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }

        private string ToJson<T>(NotificationType type, T msg)
        {
            var result = JObject.FromObject(msg, serializer);
            result["type"] = type.ToString().ToLower();
            return result.ToString(Formatting.None);
        }
    }
}
