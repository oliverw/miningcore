using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Miningcore.Blockchain.Pandanite
{
    public class PandaniteNodeV1Api : IPandaniteNodeApi
    {
        private HttpClient HttpClient { get; }
        private string Url { get; }

        public PandaniteNodeV1Api(HttpClient httpClient, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new System.ArgumentException($"'{nameof(url)}' cannot be null or empty.", nameof(url));
            }

            HttpClient = httpClient ?? throw new System.ArgumentNullException(nameof(httpClient));
            Url = url;
        }

        public async Task<(bool success, uint block)> GetBlock()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/block_count");

                using (var httpResponseMessage = await HttpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    uint block = 0;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            block = await JsonSerializer.DeserializeAsync<uint>(contentStream);
                        }
                    }

                    return (success, block);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, 0);
            }
        }

        public async Task<(bool success, MiningProblem data)> GetMiningProblem()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/mine");

                using (var httpResponseMessage = await HttpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    MiningProblem data = null;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            data = await JsonSerializer.DeserializeAsync<MiningProblem>(contentStream);
                        }
                    }

                    return (success, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, null);
            }
        }

        public async Task<(bool success, List<Transaction> data)> GetTransactions()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/tx_json");

                using (var httpResponseMessage = await HttpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    var data = new List<Transaction>();

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            await foreach (var tx in JsonSerializer.DeserializeAsyncEnumerable<Transaction>(contentStream)) {
                                data.Add(tx);
                            }
                        }
                    }
                    return (success, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, new List<Transaction>());
            }
        }

        public async Task<bool> Submit(Stream stream)
        {
            try
            {
                var content = new StreamContent(stream);

                using (var httpResponseMessage = await HttpClient.PostAsync(Url + "/submit", content))
                {
                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        var result = await httpResponseMessage.Content.ReadAsStringAsync();
                        
                        if (result.Contains("SUCCESS")) {
                            return true;
                        }

                        Console.WriteLine(result);
                        return false;
                    }

                    return httpResponseMessage.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
        }

        public async Task<(bool success, List<TransactionStatus> data)> SubmitTransactions(List<Transaction> transactions)
        {
            try
            {
                var txs = transactions.Select(tx => new TransactionInfo
                {
                    amount = tx.amount,
                    fee = tx.fee,
                    from = tx.from,
                    to = tx.to,
                    signature = tx.signature,
                    signingKey = tx.signingKey,
                    timestamp = tx.timestamp
                }).ToList();

                var json = JsonSerializer.SerializeToUtf8Bytes<List<TransactionInfo>>(txs);

                using var content = new ByteArrayContent(json);
                using var httpResponseMessage = await HttpClient.PostAsync(Url + "/add_transaction_json", content);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    var data = new List<TransactionStatus>();
                    var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();
                    
                    // HACK: FIXME: API needs to be updated to return txId as part of response
                    await foreach (var tx in JsonSerializer.DeserializeAsyncEnumerable<TransactionStatus>(contentStream)) {
                        data.Add(tx);
                    }

                    return (true, data);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, null);
            }
        }

        public async Task<(bool, Dictionary<string, string>)> VerifyTransactions(string[] txs)
        {
            try
            {
                var txIds = txs.Select(tx => new TransactionId 
                {
                    txid = tx
                }).ToList();

                var json = JsonSerializer.SerializeToUtf8Bytes<List<TransactionId>>(txIds);

                using var content = new ByteArrayContent(json);
                using var httpResponseMessage = await HttpClient.PostAsync(Url + "/verify_transaction", content);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    var data = new Dictionary<string, string>();

                    var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();

                    int i = 0;
                    
                    // HACK: FIXME: API needs to be updated to return txId as part of response
                    await foreach (var tx in JsonSerializer.DeserializeAsyncEnumerable<TransactionStatus>(contentStream)) {
                        data.TryAdd(txs[i], tx.status);
                        i++;
                    }

                    return (true, data);
                }

                return (false, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, null);
            }
        }

        public async Task<(bool success, ulong hashrate)> GetNetworkHashrate()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/getnetworkhashrate");

                using (var httpResponseMessage = await HttpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;
                    ulong hashrate = 0;

                    if (success)
                    {
                        using (var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync())
                        {
                            hashrate = await JsonSerializer.DeserializeAsync<ulong>(contentStream);
                        }
                    }

                    return (success, hashrate);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, 0);
            }
        }

        public async Task<(bool success, List<string> peers)> GetPeers()
        {
            try
            {
                var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, Url + "/peers");

                using (var httpResponseMessage = await HttpClient.SendAsync(httpRequestMessage))
                {
                    var success = httpResponseMessage.IsSuccessStatusCode;

                    if (!success) {
                        return (false, new List<string>());
                    }

                    var data = new List<string>();

                    var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();

                    await foreach (var peer in JsonSerializer.DeserializeAsyncEnumerable<string>(contentStream)) {
                        data.Add(peer);
                    }

                    return (true, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, new List<string>());
            }
        }
    }
}
