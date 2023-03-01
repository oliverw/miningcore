using System.Globalization;
using System.Threading.Tasks;
using Miningcore.Crypto.Hashing.Ethash.Ethash;
using Miningcore.Extensions;
using NLog;
using Xunit;

namespace Miningcore.Tests.Crypto;


public record TestBlock
{
    public double Difficulty { get; set; }
    public byte[] Hash { get; set; }
    public ulong Nonce { get; set; }
    public byte[] MixDigest { get; set; }
    public ulong Height { get; set; }
}

public class EthashTests : TestBase
{
    private static readonly ILogger logger = new NullLogger(LogManager.LogFactory);

    [Fact]
    public async Task Ethash_Hash()
    {
        var ethash = new EthashLight();
        ethash.Setup(3, 0);

        Assert.Equal("Ethash", ethash.AlgoName);

        var testBlocks = new TestBlock[]
        {
            new TestBlock {
                Height = 22,
                Hash = "372eca2454ead349c3df0ab5d00b0b706b23e49d469387db91811cee0358fc6d".HexToByteArray(),
                Difficulty = 132416,
                Nonce = ulong.Parse("495732e0ed7a801c", NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                MixDigest = "2f74cdeb198af0b9abe65d22d372e22fb2d474371774a9583c1cc427a07939f5".HexToByteArray(),
            },
            new TestBlock {
                Height = 30001,
                Hash = "7e44356ee3441623bc72a683fd3708fdf75e971bbe294f33e539eedad4b92b34".HexToByteArray(),
                Difficulty = 1532671,
                Nonce = ulong.Parse("318df1c8adef7e5e", NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                MixDigest = "144b180aad09ae3c81fb07be92c8e6351b5646dda80e6844ae1b697e55ddde84".HexToByteArray(),
            },
            new TestBlock {
                Height = 60000,
                Hash = "5fc898f16035bf5ac9c6d9077ae1e3d5fc1ecc3c9fd5bee8bb00e810fdacbaa0".HexToByteArray(),
                Difficulty = 2467358,
                Nonce = ulong.Parse("50377003e5d830ca", NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                MixDigest = "ab546a5b73c452ae86dadd36f0ed83a6745226717d3798832d1b20b489e82063".HexToByteArray(),
            },
        };

        var invalidBlock = new TestBlock
        {
            Height = 61439999, // 61440000 causes the c lib to crash
            Hash = "foo".HexToByteArray(),
            Difficulty = 0,
            Nonce = ulong.Parse("cafebabec00000fe", NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            MixDigest = "bar".HexToByteArray(),
        };

        // test valid blocks
        foreach(var testBlock in testBlocks)
        {
            var cache = await ethash.GetCacheAsync(logger, testBlock.Height);
            var ok = cache.Compute(logger, testBlock.Hash, testBlock.Nonce, out var mixDigest, out var result);
            Assert.True(ok);
            Assert.Equal(testBlock.MixDigest, mixDigest);
        }

        // test invalid block
        var invalidCache = await ethash.GetCacheAsync(logger, invalidBlock.Height);
        var invalidOk = invalidCache.Compute(logger, invalidBlock.Hash, invalidBlock.Nonce, out var invalidMixDigest, out var invalidResult);
        Assert.True(invalidOk);
        Assert.NotEqual(invalidBlock.MixDigest, invalidMixDigest);
    }
}