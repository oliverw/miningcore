using System;
using System.Text;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;

namespace Miningcore.Tests.Crypto;

public class RandomARQTests : TestBase
{
    const string realm = "xmr";
    private static readonly string seedHex = Encoding.UTF8.GetBytes("test key 000").ToHexString();
    private static readonly byte[] input1 = Encoding.UTF8.GetBytes("This is a test");
    private static readonly byte[] input2 = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet");
    private const string hashExpected1 = "27f66e4650eb5657513e76c140e09e59336786f21fbef1ed6ff40fc21538221e";
    private const string hashExpected2 = "6b04e883e07e4e6c072cb064d9aed0fa5a8f7cbbeb7fd3ba653d274ebd4925b0";

    [Fact]
    public void CreateAndDeleteSeed()
    {
        // creation
        RandomARQ.CreateSeed(realm, seedHex);
        Assert.True(RandomARQ.realms.ContainsKey(realm));
        Assert.True(RandomARQ.realms[realm].ContainsKey(seedHex));

        // accessing the created seed should work
        Assert.NotNull(RandomARQ.GetSeed(realm, seedHex));

        // creating the same realm and key twice should not result in duplicates
        RandomARQ.CreateSeed(realm, seedHex);
        Assert.Equal(RandomARQ.realms.Count, 1);
        Assert.Equal(RandomARQ.realms[realm].Count, 1);

        // deletion
        RandomARQ.DeleteSeed(realm, seedHex);
        Assert.False(RandomARQ.realms[realm].ContainsKey(seedHex));
    }

    [Fact]
    public void CalculateHashSlow()
    {
        var buf = new byte[32];

        // light-mode
        RandomARQ.CreateSeed(realm, seedHex);

        RandomARQ.CalculateHash("xmr", seedHex, input1, buf);
        var result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        Array.Clear(buf, 0, buf.Length);

        // second invocation should give the same result
        RandomARQ.CalculateHash("xmr", seedHex, input1, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        RandomARQ.CalculateHash("xmr", seedHex, input2, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected2, result);

        RandomARQ.DeleteSeed(realm, seedHex);
    }

    [Fact]
    public void CalculateHashFast()
    {
        var buf = new byte[32];

        // fast-mode
        RandomARQ.CreateSeed(realm, seedHex, null, RandomX.randomx_flags.RANDOMX_FLAG_FULL_MEM);

        RandomARQ.CalculateHash("xmr", seedHex, input1, buf);
        var result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        Array.Clear(buf, 0, buf.Length);

        // second invocation should give the same result
        RandomARQ.CalculateHash("xmr", seedHex, input1, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected1, result);

        RandomARQ.CalculateHash("xmr", seedHex, input2, buf);
        result = buf.ToHexString();
        Assert.Equal(hashExpected2, result);

        RandomARQ.DeleteSeed(realm, seedHex);
    }
}
