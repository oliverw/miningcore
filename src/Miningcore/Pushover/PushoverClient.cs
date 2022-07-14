using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Rest;

namespace Miningcore.Pushover;

public class PushoverClient
{
    public PushoverClient(ClusterConfig clusterConfig, IHttpClientFactory httpClientFactory)
    {
        config = clusterConfig?.Notifications.Pushover;
        client = new SimpleRestClient(httpClientFactory, PushoverConstants.ApiBaseUrl);
    }

    private readonly SimpleRestClient client;
    private readonly PushoverConfig config;

    public async Task<PushoverReponse> PushMessage(string title, string message,
        PushoverMessagePriority priority, CancellationToken ct)
    {
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(title));
        Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(message));

        var msg = new PushoverRequest
        {
            User = config.User,
            Token = config.Token,
            Title = title,
            Message = message,
            Priority = (int) priority,
            Timestamp = (int) DateTimeOffset.Now.ToUnixTimeSeconds(),
        };

        return await client.Post<PushoverReponse>("/messages.json", msg, ct);
    }
}
