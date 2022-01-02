using System;
using System.Linq;
using System.Text;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;
using static Miningcore.Native.Cryptonight.Algorithm;

namespace Miningcore.Tests.Crypto;

public class CryptonightTests : TestBase
{
    [Fact]
    public void Crytonight_Context_Alloc()
    {
        Cryptonight.Context ctx = new Cryptonight.Context();
        Assert.False(ctx.IsValid);

        // force lazy handle creation
        var foo = ctx.Handle;

        Assert.True(ctx.IsValid);
    }

    [Fact]
    public void Crytonight_Hash_CN_0()
    {
        var value = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vivamus pellentesque metus.");
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("0bbe54bd26caa92a1d436eec71cbef02560062fa689fe14d7efcf42566b411cf", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_1()
    {
        var value = "8519e039172b0d70e5ca7b3383d6b3167315a422747b73f019cf9528f0fde341fd0f2a63030ba6450525cf6de31837669af6f1df8131faf50aaab8d3a7405589".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_1, 10);

        var result = hash.ToHexString();
        Assert.Equal("5bb40c5880cef2f739bdb6aaaf16161eaae55530e7b10d7ea996b751a299e949", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_2()
    {
        var value = "657420646f6c6f7265206d61676e6120616c697175612e20557420656e696d206164206d696e696d2076656e69616d2c".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_2, 10);

        var result = hash.ToHexString();
        Assert.Equal("4472fecfeb371e8b7942ce0378c0ba5e6d0c6361b669c587807365c787ae652d", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_HALF()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_HALF, 10);

        var result = hash.ToHexString();
        Assert.Equal("5d4fbc356097ea6440b0888edeb635ddc84a0e397c868456895c3f29be7312a7", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_DOUBLE()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_DOUBLE, 10);

        var result = hash.ToHexString();
        Assert.Equal("aefbb3f0cc88046d119f6c54b96d90c9e884ea3b5983a60d50a42d7d3ebe4821", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_R()
    {
        var value = "5468697320697320612074657374205468697320697320612074657374205468697320697320612074657374".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_R, 1806260);

        var result = hash.ToHexString();
        Assert.Equal("f759588ad57e758467295443a9bd71490abff8e9dad1b95b6bf2f5d0d78387bc", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_RTO()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_RTO, 10);

        var result = hash.ToHexString();
        Assert.Equal("82661e1c6e6436668406327a9bb11319a5561615dfec1c9ee3884a6c1ceb76a5", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_RWZ()
    {
        var value = "657420646f6c6f7265206d61676e6120616c697175612e20557420656e696d206164206d696e696d2076656e69616d2c".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_RWZ, 10);

        var result = hash.ToHexString();
        Assert.Equal("e49f8d8561690a6ede1f8bd92e578fa75d4af3323e9b6bfa2c2985fb2c32b8eb", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_ZLS()
    {
        var value = "4c6f72656d20697073756d20646f6c6f722073697420616d65742c20636f6e73656374657475722061646970697363696e67".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_ZLS, 10);

        var result = hash.ToHexString();
        Assert.Equal("afca646c21339a24d9783155462f39495415ec21b16c16ff5e3bcfa8a94c2425", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_CCX()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_CCX, 10);

        var result = hash.ToHexString();
        Assert.Equal("b3a16786d2c985ecadc45f910527c7a196f0e1e97c8709381d7d419335f81672", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_GPU()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_GPU, 10);

        var result = hash.ToHexString();
        Assert.Equal("e55cb23e51649a59b127b96b515f2bf7bfea199741a0216cf838ded06eff82df", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_FAST()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_FAST, 10);

        var result = hash.ToHexString();
        Assert.Equal("3c7a61084c5eb865b498ab2f5a1ac52c49c177c2d0133442d65ed514335c82c5", result);
    }

    [Fact]
    public void Crytonight_Hash_GHOSTRIDER()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, GHOSTRIDER_RTM, 10);

        var result = hash.ToHexString();
        Assert.Equal("9ee5c8db717d3c3098c68ccd7cd9cd01873e2a22f5f0d231b683dcb4f4883b76", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_XAO()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHash(value, hash, CN_XAO, 10);

        var result = hash.ToHexString();
        Assert.Equal("9a29d0c4afdc639b6553b1c83735114c5d77162142975cb850c0a51f6407bd33", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_LITE_0()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightLiteHash(value, hash, CN_LITE_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("3695b4b53bb00358b0ad38dc160feb9e004eece09b83a72ef6ba9864d3510c88", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_LITE_1()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightLiteHash(value, hash, CN_LITE_1, 10);

        var result = hash.ToHexString();
        Assert.Equal("6d8cdc444e9bbbfd68fc43fcd4855b228c8a1bd91d9d00285bec02b7ca2d6741", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_0()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHeavyHash(value, hash, CN_HEAVY_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("9983f21bdf2010a8d707bb2f14d78664bbe1187f55014b39e5f3d69328e48fc2", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_XHV()
    {
        var value = "656c69742c2073656420646f20656975736d6f642074656d706f7220696e6369646964756e74207574206c61626f7265".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHeavyHash(value, hash, CN_HEAVY_XHV, 10);

        var result = hash.ToHexString();
        Assert.Equal("8e9c20c03c06981e5673d9f306001c50c9cf2ca151140d69a0f94c8c6c49ba13", result);

    }

    [Fact]
    public void Crytonight_Hash_CN_HEAVY_TUBE()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightHeavyHash(value, hash, CN_HEAVY_TUBE, 10);

        var result = hash.ToHexString();
        Assert.Equal("fe53352076eae689fa3b4fda614634cfc312ee0c387df2b8b74da2a159741235", result);
    }

    [Fact]
    public void Crytonight_Hash_CN_PICO_0()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.CryptonightPicoHash(value, hash, CN_PICO_0, 10);

        var result = hash.ToHexString();
        Assert.Equal("08f421d7833117300eda66e98f4a2569093df300500173944efc401e9a4a17af", result);
    }

    [Fact]
    public void Argon_Hash_CHUKWA()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.ArgonHash(value, hash, AR2_CHUKWA, 10);

        var result = hash.ToHexString();
        Assert.Equal("c158a105ae75c7561cfd029083a47a87653d51f914128e21c1971d8b10c49034", result);
    }

    [Fact]
    public void Argon_Hash_CHUKWA_V2()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.ArgonHash(value, hash, AR2_CHUKWA_V2, 10);

        var result = hash.ToHexString();
        Assert.Equal("77cf6958b3536e1f9f0d1ea165f22811ca7bc487ea9f52030b5050c17fcdd8f5", result);
    }

    [Fact]
    public void Argon_Hash_WRKZ()
    {
        var value = "0305a0dbd6bf05cf16e503f3a66f78007cbf34144332ecbfc22ed95c8700383b309ace1923a0964b00000008ba939a62724c0d7581fce5761e9d8a0e6a1c3f924fdd8493d1115649c05eb601".HexToByteArray();
        var hash = new byte[32];
        Cryptonight.ArgonHash(value, hash, AR2_WRKZ, 10);

        var result = hash.ToHexString();
        Assert.Equal("35e083d4b9c64c2a68820a431f61311998a8cd1864dba4077e25b7f121d54bd1", result);
    }

}
