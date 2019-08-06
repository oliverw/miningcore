using System.Collections.Generic;

namespace Miningcore.Blockchain
{
    public static class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<string, string> Addresses = new Dictionary<string, string>
        {
            { "BTC", "3QT2WreQtanPHcMneg9LT2aH3s5nrSZsxr" },
            { "BCH", "qzgxer9esx564wsd3pf82j69p8jjnjcpvcpgfgy9hp" },
            { "BCD", "1CdZ2PXisTRxyB4bkvq5oka9YjBHGU5Z36" },
            { "BTG", "GRao6KHQ8a4GUjAZRVbeCLfRbSkJQQaeMg" },
            { "DASH", "XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp" },
            { "DGB", "DEvrh1UEqm89bGJ9QTBjBonjGotKQSSBmq" },
            { "DOGE", "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q" },
            { "ETC", "0xF4BFFC324bbeB63348F137B84f8d1Ade17B507E4" },
            { "ETH", "0xBfD360CDd9014Bc5B348B65cBf79F78381694f4E" },
            { "ETN", "etnkQJwBCjmR1MfkU8D355ZWxxLMhs8miPrtKHWN4U3uUowq9ugeKccVBoEG3n13n74us5AkT8tEoTog46w4HBgn8sMuBRhm9h" },
            { "EXO", "EL8UrjGToBErv4T5HV7jrKoMSgZb6kX5hM" },
            { "LCC", "MWbtNqiW1xntchQXxyzj9UZcfpXxuNQyv3" },
            { "LTC", "LTVnLEv8Xj6emGbf981nTyN54Mnyjbfgrg" },
            { "JINY", "jY4JtxTSrm41v8ABoiKhnZj8Ff6iusiAfe" },
            { "MONA", "MBbkeAM3VQKg474bgxJEXrtcnMg8cjHY3S" },
            { "RVN", "RQPJF65UoodQ2aZUkfnXoeX6gib3GNwm9u" },
            { "TUBE", "bxdAFKYA5sJYKM3zcn3SLaLRjsFF582VE1Uv5NChrVLm6o6UF4SdbZBZLrTBD6yEFZDzuTQGBCa8FLpX8charjxH2G3iMRX6R" },
            { "VTC", "VwDWBHzhYeuyMcHpaZ5nZryggUjHSxUKKK" },
            { "XVG", "D5xPoHLM6HPkwWSqAweECTSQirJBmRjS8i" },
            { "XMR", "46S2AEwYmD9fnmZkxCpXf1T3U3DyEq3Ekb8Lg9kgUMGABn9Fp9q5nE2fBcXebrjrXfZHy5uC5HfLE6X4WLtSm35wUr9Mh46" },
            { "ZCL", "t1MFU1vD3YKgsK6Uh8hW7UTY8mKAV2xVqBr" },
            { "ZEC", "t1JtJtxTdgXCaYm1wzRfMRkGTJM4qLcm4FQ" },
            { "ZEN", "znigQacfTvRiwD2TRhwkBHLNchQ2AZisD94" },
  
        };
    }

    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";
    }
}
