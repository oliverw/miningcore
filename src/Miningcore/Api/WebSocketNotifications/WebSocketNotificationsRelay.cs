using System.Net.WebSockets;
using System.Reactive.Linq;
using Autofac;
using Miningcore.Api.WebSocketNotifications;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using WebSocketManager;
using WebSocketManager.Common;

namespace Miningcore.Api;

public class WebSocketNotificationsRelay : WebSocketHandler
{
    public WebSocketNotificationsRelay(WebSocketConnectionManager webSocketConnectionManager, IComponentContext ctx) :
        base(webSocketConnectionManager, new StringMethodInvocationStrategy())
    {
        messageBus = ctx.Resolve<IMessageBus>();
        var clusterConfig = ctx.Resolve<ClusterConfig>();
        pools = clusterConfig.Pools.ToDictionary(x => x.Id, x => x);

        serializer = new JsonSerializer
        {
            ContractResolver = ctx.Resolve<JsonSerializerSettings>().ContractResolver!
        };

        Relay<BlockFoundNotification>(WsNotificationType.BlockFound);
        Relay<BlockUnlockedNotification>(WsNotificationType.BlockUnlocked);
        Relay<BlockConfirmationProgressNotification>(WsNotificationType.BlockUnlockProgress);
        Relay<NewChainHeightNotification>(WsNotificationType.NewChainHeight);
        Relay<PaymentNotification>(WsNotificationType.Payment);
        Relay<HashrateNotification>(WsNotificationType.HashrateUpdated);
    }

    private readonly IMessageBus messageBus;
    private readonly Dictionary<string, PoolConfig> pools;
    private readonly JsonSerializer serializer;
    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

    public override async Task OnConnected(WebSocket socket)
    {
        WebSocketConnectionManager.AddSocket(socket);

        var greeting = ToJson(WsNotificationType.Greeting, new { Message = "Connected to Miningcore notification relay" });
        await socket.SendAsync(greeting, CancellationToken.None);
    }

    private void Relay<T>(WsNotificationType type)
    {
        messageBus.Listen<T>()
            .Select(x => Observable.FromAsync(() => BroadcastNotification(type, x)))
            .Concat()
            .Subscribe();
    }

    private async Task BroadcastNotification<T>(WsNotificationType type, T notification)
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

        catch(Exception ex)
        {
            logger.Error(ex);
        }
    }

    private string ToJson<T>(WsNotificationType type, T msg)
    {
        var result = JObject.FromObject(msg, serializer);
        result["type"] = type.ToString().ToLower();

        return result.ToString(Formatting.None);
    }
}
