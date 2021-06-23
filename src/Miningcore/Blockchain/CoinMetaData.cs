using System.Collections.Generic;

namespace Miningcore.Blockchain
{
    public static class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<string, string> Addresses = new()
        {
            { "BTC", "bc1quzdczlpfn3n4xvpdz0x9h79569afhg0ashwxxp" },
            { "BCH", "qrf6uhhapq7fgkjv2ce2hcjqpk8ec2zc25et4xsphv" },
            { "BCHA", "14ozn3Qk8F2D8nyhwVBYTK5RBShsPLCNAL" },
            { "BCD", "1CdZ2PXisTRxyB4bkvq5oka9YjBHGU5Z36" },
            { "LTC", "LTK6CWastkmBzGxgQhTTtCUjkjDA14kxzC" },
            { "LCC", "MWbtNqiW1xntchQXxyzj9UZcfpXxuNQyv3" },
            { "DOGE", "DGDuKRhBewGP1kbUz4hszNd2p6dDzWYy9Q" },
            { "DGB", "DEvrh1UEqm89bGJ9QTBjBonjGotKQSSBmq" },
            { "ETH", "0xcb55abBfe361B12323eb952110cE33d5F28BeeE1" },
            { "ETC", "0xF8cCE9CE143C68d3d4A7e6bf47006f21Cfcf93c0" },
            { "DASH", "XqpBAV9QCaoLnz42uF5frSSfrJTrqHoxjp" },
            { "MONA", "MBbkeAM3VQKg474bgxJEXrtcnMg8cjHY3S" },
            { "VTC", "VwDWBHzhYeuyMcHpaZ5nZryggUjHSxUKKK" },
            { "ZEC", "t1YEgm6ovXFseeFxXgFY2zXxwsScD4BbfhT" },
            { "BTG", "GRao6KHQ8a4GUjAZRVbeCLfRbSkJQQaeMg" },
            { "XVG", "D5xPoHLM6HPkwWSqAweECTSQirJBmRjS8i" },
            { "XMR", "46S2AEwYmD9fnmZkxCpXf1T3U3DyEq3Ekb8Lg9kgUMGABn9Fp9q5nE2fBcXebrjrXfZHy5uC5HfLE6X4WLtSm35wUr9Mh46" },
            { "RVN", "RF8wbxb3jeAcrH2z71NxccmyZmufk1D5m5" },
            { "TUBE", "bxdAFKYA5sJYKM3zcn3SLaLRjsFF582VE1Uv5NChrVLm6o6UF4SdbZBZLrTBD6yEFZDzuTQGBCa8FLpX8charjxH2G3iMRX6R" },
        };
    }

    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";
    }
}
