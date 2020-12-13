using System.Collections.Generic;

namespace Miningcore.Blockchain
{
    public static class DevDonation
    {
        public const decimal Percent = 0.1m;

        public static readonly Dictionary<string, string> Addresses = new Dictionary<string, string>
        {
            { "BTC", "3QT2WreQtanPHcMneg9LT2aH3s5nrSZsxr" },
            { "BCH", "1EAeLnnNSPAzQuu39LQDrxv7qpZKJ3HyGy" },
            { "BCD", "1P1HstGYQTzGwbFHo7J57XCn5eFmxtMyou" },
            { "BTG", "GWRdfjqne9DxvQcbd9Ebn7vz2ngimuxfhD" },
            { "DASH", "Xc2vm9SfRn8t1hyQgqi8Zrt3oFeGcQtwTh" },
            { "DOGE", "DHionsTUxhWxNwVuRna3u7kgzkdB56YBUS" },
            { "DGB", "dgb1qyg8gclh7pymgqyckjx463np05m2g5whend0l3j" },
            { "ETC", "0xF4BFFC324bbeB63348F137B84f8d1Ade17B507E4" },
            { "ETH", "0xBfD360CDd9014Bc5B348B65cBf79F78381694f4E" },
            { "ETN", "etnk6EuyHNSd4inpVtVykgcWr3u4PD3gCfByaQDKTArKKHzbdYqRULM6ZNuPFgMn4X9Mo7mtfFKj76NecMaAsXEZ64gnxqLrFk" },
            { "LCC", "CJ5paRv11tWS63dhoTHVdWKKjJuicvxSzb" },
            { "LTC", "LTVnLEv8Xj6emGbf981nTyN54Mnyjbfgrg" },
            { "ETP", "MPJ8KGDoYJPUUcdZ8skpqRis8sVGFpZert" },
            { "MONA", "mona1qejrhuj83zxwrsjuvpd89ylt2nh4sjccux4uh0q" },
            { "RVN", "REYo1axeDk7V8BAJZ9JYyChpFWFZDp8dgJ" },
            { "TUBE", "bxcBHCGkPubPLMX5bHk3HGU83sMB6nmWTfmHBqmHLq2ZPECUCXtCBcxJFpmWgEadDu1xy26ECQ1RRgQcV4BeoGGv2YeZWJmWk" },
            { "VTC", "RGJt1Ti3LS9J1Zp4Z7xAZGTXiCRTVWiB9a" },
            { "XVG", "DGSwZ64uu1aVAoopiSEGa6iRChDWy6QTQD" },
            { "XMR", "44riGcQcDp4EsboDJP284CFCnJ2qP7y8DAqGC4D9WtVbEqzxQ3qYXAUST57u5FkrVF7CXhsEc63QNWazJ5b9ygwBJBtB2kT" },
            { "ZCL", "t1WTKFwvydcQGSaNCddPcVAh1NH5xUyJnJD" },
            { "ZEC", "t1JtJtxTdgXCaYm1wzRfMRkGTJM4qLcm4FQ" },
            { "ZEN", "znhexRavXshuP8bYeLPKPi442AuStTWUSfY" }
        };
    }

    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";
    }
}
