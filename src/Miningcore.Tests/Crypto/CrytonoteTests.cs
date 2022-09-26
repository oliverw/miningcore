using System;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;

namespace Miningcore.Tests.Crypto;

public class CrytonoteTests : TestBase
{
    [Fact]
    public void Crytonote_ConvertBlob()
    {
        var blob = "0106e5b3afd505583cf50bcc743d04d831d2b119dc94ad88679e359076ee3f18d258ee138b3b421c0300a401d90101ff9d0106d6d6a88702023c62e43372a58cb588147e20be53a27083f5c522f33c722b082ab7518c48cda280b4c4c32102609ec96e2499ee267d70efefc49f26e330526d3ef455314b7b5ba268a6045f8c80c0fc82aa0202fe5cc0fa56c4277d1a47827edce4725571529d57f33c73ada481ef84c323f30a8090cad2c60e02d88bf5e72a611c8b8464ce29e3b1adbfe1ae163886d9150fe511171cada98fcb80e08d84ddcb0102441915aaf9fbaf70ff454c701a6ae2bd59bb94dc0b888bf7e5d06274ee9238ca80c0caf384a302024078526e2132def44bde2806242652f5944e632f7d94290dd6ee5dda1929f5ee2b016e29f25f07ec2a8df59f0e118a6c9a4b769b745dc0c729071f6e0399d2585745020800000000012e7f76000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000".HexToByteArray();

        var result = CryptonoteBindings.ConvertBlob(blob, 330).ToHexString();
        Assert.Equal("0106e5b3afd505583cf50bcc743d04d831d2b119dc94ad88679e359076ee3f18d258ee138b3b421c0300a4487286e262e95b8d2163a0c8b73527e8c9425adbdc4e532cf0ef4241f9ffbe9e01", result);
    }

    [Fact]
    public void Crytonote_DecodeAddress()
    {
        var address = "48nhyWcSey31ngSEhV8j8NPm6B8PistCQJBjjDjmTvRSTWYg6iocAw131vE2JPh3ps33vgQDKLrUx3fcErusYWcMJBxpm1d";
        var result = CryptonoteBindings.DecodeAddress(address);

        Assert.Equal(18ul, result);
    }

    [Fact]
    public void Crytonote_DecodeSubAddress()
    {
        var address = "84k5FLcuZeQ9vUmTfRJkpxCxdppVF5wWdPpxhU4SdTZmAD1i1YH81rPf8XRAsbpc7Na4GG7A8xscjQbqMETLZCXZ7Cdfb7X";
        var result = CryptonoteBindings.DecodeAddress(address);

        Assert.Equal(42ul, result);
    }

    [Fact]
    public void Cryptonote_DecodeAddress_Should_Throw_On_Null_Or_Empty_Argument()
    {
        Assert.Throws<ArgumentException>(() => CryptonoteBindings.DecodeAddress(null));
        Assert.Throws<ArgumentException>(() => CryptonoteBindings.DecodeAddress(""));
    }

    [Fact]
    public void Crytonote_DecodeIntegratedAddress()
    {
        var address = "4BrL51JCc9NGQ71kWhnYoDRffsDZy7m1HUU7MRU4nUMXAHNFBEJhkTZV9HdaL4gfuNBxLPc3BeMkLGaPbF5vWtANQsGwTGg55Kq4p3ENE7";
        var result = CryptonoteBindings.DecodeIntegratedAddress(address);

        Assert.Equal(19ul, result);
    }

    [Fact]
    public void Cryptonote_CryptonightHashFast()
    {
        var value = "8519e039172b0d70e5ca7b3383d6b3167315a422747b73f019cf9528f0fde341fd0f2a63030ba6450525cf6de31837669af6f1df8131faf50aaab8d3a7405589".HexToByteArray();
        var hash = new byte[32];
        CryptonoteBindings.CryptonightHashFast(value, hash);

        var result = hash.ToHexString();
        Assert.Equal("daedcec20429ffd440ab70a0a3a549fbc89745581f539fd2ac945388698e2db2", result);
    }
}
