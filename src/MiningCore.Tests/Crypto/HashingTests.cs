using System;
using System.Linq;
using System.Text;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Extensions;
using Xunit;

namespace MiningCore.Tests.Crypto
{
    public class HashingTests : TestBase
    {
        private static readonly byte[] testValue = Enumerable.Repeat((byte) 0x80, 32).ToArray();

        [Fact]
        public void Blake_Hash_Should_Match()
        {
            var hasher = new Blake();
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "a5adc5e82053fec28c92c31e3f17c3cfe761ddcb9435ba377671ea86a4a9e83e");
        }

        [Fact]
        public void Blake_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Blake();
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }

        [Fact]
        public void Groestl_Hash_Should_Match()
        {
            var hasher = new Groestl();
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "e14c0b9b145f2df8ebf37c81a4982a87e174a8b46c7e5ca9326d10997e02e133");
        }

        [Fact]
        public void Groestl_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Groestl();
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }

        [Fact]
        public void Kezzak_Hash_Should_Match()
        {
            var hasher = new Kezzak();
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "00b11e72b948db16a181437150237fa247f9b5932758b7d3f648832ed88e7919");
        }

        [Fact]
        public void Kezzak_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Kezzak();
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }

        [Fact]
        public void Scrypt_Hash_Should_Match()
        {
            var hasher = new Scrypt(1024, 1);
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "b546d334422ff5fff98e8ba847a55bbc06271c64bb5e21107b1b225f6579d40a");
        }

        [Fact]
        public void Scrypt_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Scrypt(1024, 1);
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }

        [Fact]
        public void Sha256D_Hash_Should_Match()
        {
            var hasher = new Sha256D();
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "4f4eb6dbba8198745a278997e154e8309b571259e33fce4d3a31adea39dc9173");
        }

        [Fact]
        public void Sha256D_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Sha256D();
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }

        [Fact]
        public void Sha256S_Hash_Should_Match()
        {
            var hasher = new Sha256S();
            var result = hasher.Digest(testValue, 0).ToHexString();

            Assert.Equal(result, "bd75a82b9957d6d043076dea52262635042693f1fe23bcadadaecc908e1e5cc6");
        }

        [Fact]
        public void Sha256S_Hash_Should_Throw_On_Null_Argument()
        {
            var hasher = new Sha256S();
            Assert.Throws<ArgumentNullException>(() => hasher.Digest(null, 0));
        }
    }
}
