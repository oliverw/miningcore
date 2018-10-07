using System.Collections.Generic;

namespace Miningcore.Blockchain
{
    public class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<string, string> Addresses = new Dictionary<string, string>
        {
            { "BTC", "17QnVor1B6oK1rWnVVBrdX9gFzVkZZbhDm" },
            { "BCH", "1LJGTzNDTuTvkHpTxNSdmAEBAXAnEHDVqQ" },
            { "BCD", "1CdZ2PXisTRxyB4bkvq5oka9YjBHGU5Z36" },
            { "LTC", "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC" },
            { "DOGE", "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q" },
            { "NMC", "NDSLDpFEcTbuRVcWHdJyiRZThVAcb5Z79o" },
            { "DGB", "DAFtYMGVdNtqHJoBGg2xqZZwSuYAaEs2Bn" },
            { "ETH", "0xcb55abBfe361B12323eb952110cE33d5F28BeeE1" },
            { "ETC", "0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0" },
            { "PPC", "PE8RH6HAvi8sqYg47D58TeKTjyeQFFHWR2" },
            { "DASH", "XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp" },
            { "VIA", "Vc5rJr2QdA2yo1jBoqYUAH7T59uBh2Vw5q" },
            { "MONA", "MBbkeAM3VQKg474bgxJEXrtcnMg8cjHY3S" },
            { "VTC", "VwDWBHzhYeuyMcHpaZ5nZryggUjHSxUKKK" },
            { "ZEC", "t1YHZHz2DGVMJiggD2P4fBQ2TAPgtLSUwZ7" },
            { "ZCL", "t1MFU1vD3YKgsK6Uh8hW7UTY8mKAV2xVqBr" },
            { "ZEN", "znigQacfTvRiwD2TRhwkBHLNchQ2AZisD94" },
            { "BTG", "GRao6KHQ8a4GUjAZRVbeCLfRbSkJQQaeMg" },
            { "MOON", "2QvpGimMYLyqKsczQXZjv56h6me3M8orwj" },
            { "XVG", "D5xPoHLM6HPkwWSqAweECTSQirJBmRjS8i" },
            { "XMR", "475YVJbPHPedudkhrcNp1wDcLMTGYusGPF5fqE7XjnragVLPdqbCHBdZg3dF4dN9hXMjjvGbykS6a77dTAQvGrpiQqHp2eH" },
            { "ETN", "etnkQJwBCjmR1MfkU8D355ZWxxLMhs8miPrtKHWN4U3uUowq9ugeKccVBoEG3n13n74us5AkT8tEoTog46w4HBgn8sMuBRhm9h" },
            { "RVN", "RQPJF65UoodQ2aZUkfnXoeX6gib3GNwm9u" },
            { "PGN", "PRm3ThUGfmU157NwcKzKBqWbgA2DPuFje1" },
        };
    }

    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";
    }
}
