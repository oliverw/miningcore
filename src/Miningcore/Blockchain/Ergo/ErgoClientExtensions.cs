using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Miningcore.Blockchain.Ergo
{
    public partial class ErgoClient
    {
        public string ApiKey { get; set; }

        private Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder url)
        {
            request.Headers.Add("api_key", ApiKey);

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
}
