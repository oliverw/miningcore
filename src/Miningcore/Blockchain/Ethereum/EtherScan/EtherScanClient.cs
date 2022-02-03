using Miningcore.Configuration;
using Miningcore.Rest;
using Miningcore.Util;
using NLog;

namespace Miningcore.Blockchain.Ethereum.EtherScan
{
    internal class EtherScanClient
    {
        private const string DateFormat = "yyyy-MM-dd";
        private readonly string apiKey;
        private readonly SimpleRestClient client;
        private readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public EtherScanClient(ClusterConfig config, IHttpClientFactory httpClientFactory)
        {
            if(config?.Pools == null) throw new ArgumentNullException(nameof(config));
            var ethPoolConfig = config.Pools.FirstOrDefault(p => p.Coin.Equals(CoinFamily.Ethereum.ToString(), StringComparison.OrdinalIgnoreCase));

            if(ethPoolConfig?.EtherScan == null) throw new ArgumentNullException(nameof(config));
            apiKey = ethPoolConfig.EtherScan.ApiKey;
            client = new SimpleRestClient(httpClientFactory, ethPoolConfig.EtherScan.ApiUrl);
        }

        public async Task<DailyBlkCount[]> GetDailyBlockCount(DateTime start, DateTime end)
        {
            var resp = await TelemetryUtil.TrackDependency(() => client.Get<EtherScanResponse<DailyBlkCount[]>>(
                // Path
                "/",
                
                CancellationToken.None,

                // Query params
                new Dictionary<string, string>()
                {
                    {"module", "stats"},
                    {"action", "dailyblkcount"},
                    {"startdate", start.ToString(DateFormat)},
                    {"enddate", end.ToString(DateFormat)},
                    {"sort", "desc"},
                    {"apikey", apiKey}
                },

                // Headers
                new Dictionary<string, string>()
                {
                    {WebConstants.HeaderAccept, WebConstants.ContentTypeText}
                }
                ),

                DependencyType.EtherScan, nameof(GetDailyBlockCount), $"st:{start},end:{end}");

            if(resp?.Status > 0) return resp.Result;

            logger.Error($"GetDailyUncleBlockCount failed. reason={resp?.Message}, status={resp?.Status}");
            return null;
        }

        public async Task<DailyBlkCount[]> GetDailyBlockCount(int lookBackDays)
        {
            return await GetDailyBlockCount(DateTime.Today.AddDays(-lookBackDays), DateTime.Today);
        }

        public async Task<DailyAverageBlockTime[]> GetDailyAverageBlockTime(DateTime start, DateTime end)
        {
            var resp = await TelemetryUtil.TrackDependency(() => client.Get<EtherScanResponse<DailyAverageBlockTime[]>>(
                // Path
                "/", 

                CancellationToken.None,

                // Query params
                new Dictionary<string, string>()
                {
                    {"module", "stats"},
                    {"action", "dailyavgblocktime"},
                    {"startdate", start.ToString(DateFormat)},
                    {"enddate", end.ToString(DateFormat)},
                    {"sort", "desc"},
                    {"apikey", apiKey}
                },

                // Headers
                new Dictionary<string, string>()
                {
                    {WebConstants.HeaderAccept, WebConstants.ContentTypeText}
                }),

                DependencyType.EtherScan, nameof(GetDailyAverageBlockTime), $"st:{start},end:{end}");

            if(resp?.Status > 0) return resp.Result;

            logger.Error($"GetDailyAverageBlockTime failed. reason={resp?.Message}, status={resp?.Status}");
            return null;
        }

        public async Task<DailyAverageBlockTime[]> GetDailyAverageBlockTime(int lookBackDays)
        {
            return await GetDailyAverageBlockTime(DateTime.Today.AddDays(-lookBackDays), DateTime.Today);
        }
    }
}
