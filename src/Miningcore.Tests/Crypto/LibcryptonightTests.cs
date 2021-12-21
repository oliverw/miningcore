using System;
using System.Linq;
using System.Text;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;

namespace Miningcore.Tests.Crypto;

public class LibcryptonightTests : TestBase
{
    [Fact]
    public void Crytonight_Context_Alloc()
    {
        libcryptonight.Context ctx = new libcryptonight.Context();
        Assert.True(ctx.IsValid);
    }

    [Fact]
    public void Crytonight_Context_Dispose()
    {
        libcryptonight.Context ctx = new libcryptonight.Context();
        ctx.Dispose();
        Assert.False(ctx.IsValid);
    }

    [Fact]
    public void Crytonight_Context_Init()
    {
        var count = 1;
        libcryptonight.ContextInit(count);
        Assert.Equal(libcryptonight.contexts.Count, count);

        libcryptonight.ContextDispose();
        Assert.Null(libcryptonight.contexts);
    }

    [Fact]
    public void Crytonight_Hash_CN_0()
    {
        libcryptonight.ContextInit(1);

        var value = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vivamus pellentesque metus.");
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("0bbe54bd26caa92a1d436eec71cbef02560062fa689fe14d7efcf42566b411cf", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_1()
    {
        libcryptonight.ContextInit(1);

        var value = "8519e039172b0d70e5ca7b3383d6b3167315a422747b73f019cf9528f0fde341fd0f2a63030ba6450525cf6de31837669af6f1df8131faf50aaab8d3a7405589".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_1, 10);

        var result = hash.ToHexString();
        Assert.Equal("5bb40c5880cef2f739bdb6aaaf16161eaae55530e7b10d7ea996b751a299e949", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_2()
    {
        libcryptonight.ContextInit(1);

        var value = "657420646f6c6f7265206d61676e6120616c697175612e20557420656e696d206164206d696e696d2076656e69616d2c".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_2, 10);

        var result = hash.ToHexString();
        Assert.Equal("4472fecfeb371e8b7942ce0378c0ba5e6d0c6361b669c587807365c787ae652d", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_R()
    {
        libcryptonight.ContextInit(1);

        var value = "5468697320697320612074657374205468697320697320612074657374205468697320697320612074657374".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_R, 10);

        var result = hash.ToHexString();
        Assert.Equal("c6bcd03a96dd5d8302c33c250bc96fc7213f9c7e497283a56e979bf2bbca3dfb", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_RTO()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_RTO, 10);

        var result = hash.ToHexString();
        Assert.Equal("82661e1c6e6436668406327a9bb11319a5561615dfec1c9ee3884a6c1ceb76a5", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_RWZ()
    {
        libcryptonight.ContextInit(1);

        var value = "657420646f6c6f7265206d61676e6120616c697175612e20557420656e696d206164206d696e696d2076656e69616d2c".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_RWZ, 10);

        var result = hash.ToHexString();
        Assert.Equal("e49f8d8561690a6ede1f8bd92e578fa75d4af3323e9b6bfa2c2985fb2c32b8eb", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_CCX()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_CCX, 10);

        var result = hash.ToHexString();
        Assert.Equal("b3a16786d2c985ecadc45f910527c7a196f0e1e97c8709381d7d419335f81672", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_GPU()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_GPU, 10);

        var result = hash.ToHexString();
        Assert.Equal("e55cb23e51649a59b127b96b515f2bf7bfea199741a0216cf838ded06eff82df", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_FAST()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_FAST, 10);

        var result = hash.ToHexString();
        Assert.Equal("3c7a61084c5eb865b498ab2f5a1ac52c49c177c2d0133442d65ed514335c82c5", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_GHOSTRIDER()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.GHOSTRIDER_RTM, 10);

        var result = hash.ToHexString();
        Assert.Equal("9ee5c8db717d3c3098c68ccd7cd9cd01873e2a22f5f0d231b683dcb4f4883b76", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_XAO()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.Cryptonight(value, hash, libcryptonight.Algorithm.CN_XAO, 10);

        var result = hash.ToHexString();
        Assert.Equal("9a29d0c4afdc639b6553b1c83735114c5d77162142975cb850c0a51f6407bd33", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_LITE_0()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.CryptonightLite(value, hash, libcryptonight.Algorithm.CN_LITE_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("3695b4b53bb00358b0ad38dc160feb9e004eece09b83a72ef6ba9864d3510c88", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_LITE_1()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.CryptonightLite(value, hash, libcryptonight.Algorithm.CN_LITE_1, 10);

        var result = hash.ToHexString();
        Assert.Equal("6d8cdc444e9bbbfd68fc43fcd4855b228c8a1bd91d9d00285bec02b7ca2d6741", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_0()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.CryptonightHeavy(value, hash, libcryptonight.Algorithm.CN_HEAVY_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("9983f21bdf2010a8d707bb2f14d78664bbe1187f55014b39e5f3d69328e48fc2", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_XHV()
    {
        libcryptonight.ContextInit(1);

        var value = "656c69742c2073656420646f20656975736d6f642074656d706f7220696e6369646964756e74207574206c61626f7265".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.CryptonightHeavy(value, hash, libcryptonight.Algorithm.CN_HEAVY_XHV, 10);

        var result = hash.ToHexString();
        Assert.Equal("8e9c20c03c06981e5673d9f306001c50c9cf2ca151140d69a0f94c8c6c49ba13", result);

        libcryptonight.ContextDispose();
    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_TUBE()
    {
        libcryptonight.ContextInit(1);

        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        libcryptonight.CryptonightHeavy(value, hash, libcryptonight.Algorithm.CN_HEAVY_TUBE, 10);

        var result = hash.ToHexString();
        Assert.Equal("fe53352076eae689fa3b4fda614634cfc312ee0c387df2b8b74da2a159741235", result);

        libcryptonight.ContextDispose();
    }

}
