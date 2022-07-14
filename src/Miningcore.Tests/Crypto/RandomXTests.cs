using System;
using System.Text;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;

namespace Miningcore.Tests.Crypto;

public class RandomXTests : TestBase
{
    const string realm = "xmr";
    private static readonly string seedHex = Encoding.UTF8.GetBytes("test key 000").ToHexString();
    private static readonly byte[] input1 = Encoding.UTF8.GetBytes("This is a test");
    private static readonly byte[] input2 = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet");
    private const string hashExpected1 = "639183aae1bf4c9a35884cb46b09cad9175f04efd7684e7262a0ac1c2f0b4e3f";
    private const string hashExpected2 = "300a0adb47603dedb42228ccb2b211104f4da45af709cd7547cd049e9489c969";

    [Fact]
    public void CreateAndDeleteSeed()
    {
        // creation
        RandomX.CreateSeed(realm, seedHex);
        Assert.True(RandomX.realms.ContainsKey(realm));
        Assert.True(RandomX.realms[realm].ContainsKey(seedHex));

        // accessing the created seed should work
        Assert.NotNull(RandomX.GetSeed(realm, seedHex));

        // creating the same realm and key twice should not result in duplicates
        RandomX.CreateSeed(realm, seedHex);
        Assert.Equal(RandomX.realms.Count, 1);
        Assert.Equal(RandomX.realms[realm].Count, 1);

        // deletion
        RandomX.DeleteSeed(realm, seedHex);
        Assert.False(RandomX.realms[realm].ContainsKey(seedHex));
    }

    [Fact]
    public void CalculateHashSlow()
    {
        var buf = new byte[32];

        // light-mode
        RandomX.CreateSeed(realm, seedHex);

        RandomX.CalculateHash("xmr", seedHex, input1, buf);
        var result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        Array.Clear(buf, 0, buf.Length);

        // second invocation should give the same result
        RandomX.CalculateHash("xmr", seedHex, input1, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        RandomX.CalculateHash("xmr", seedHex, input2, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected2, result);

        RandomX.DeleteSeed(realm, seedHex);
    }

    [Fact]
    public void CalculateHashFast()
    {
        var buf = new byte[32];

        // fast-mode
        RandomX.CreateSeed(realm, seedHex, null, RandomX.randomx_flags.RANDOMX_FLAG_FULL_MEM);

        RandomX.CalculateHash("xmr", seedHex, input1, buf);
        var result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        Array.Clear(buf, 0, buf.Length);

        // second invocation should give the same result
        RandomX.CalculateHash("xmr", seedHex, input1, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        RandomX.CalculateHash("xmr", seedHex, input2, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected2, result);

        RandomX.DeleteSeed(realm, seedHex);
    }
}
