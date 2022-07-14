using System.Text;

namespace Miningcore.Blockchain.Ergo;

public partial class ErgoClient
{
    public Dictionary<string, string> RequestHeaders { get; } = new();

    private Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder url, CancellationToken ct)
    {
        foreach(var pair in RequestHeaders)
            request.Headers.Add(pair.Key, pair.Value);

        return Task.CompletedTask;
    }

    private Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, String url, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    private Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, string url)
    {
        return Task.CompletedTask;
    }

    private static Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
