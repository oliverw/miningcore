using System.Text.Json.Serialization;

namespace Miningcore.Blockchain.Ethereum.EtherScan
{
    public class EtherScanResponse<T>
    {
        public int Status { get; set; }
        public string Message { get; set; }
        public T Result { get; set; }
    }

    public class DailyBlkCount
    {
        public DateTime UtcDate { get; set; }
        public string UnixTimeStamp { get; set; }
        public long BlockCount { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonPropertyName("blockRewards_Eth")]
        public decimal BlockRewardsEth { get; set; }
    }

    public class DailyAverageBlockTime
    {
        public DateTime UtcDate { get; set; }
        public string UnixTimeStamp { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonPropertyName("blockTime_sec")]
        public double BlockTimeSec { get; set; }
    }
}
