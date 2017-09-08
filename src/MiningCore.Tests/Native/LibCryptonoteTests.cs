using System;
using System.Linq;
using System.Text;
using MiningCore.Extensions;
using MiningCore.Native;
using Xunit;

namespace MiningCore.Tests.Native
{
    public class HashinLibCryptonoteTestsgTests : TestBase
    {
        private static readonly byte[] testValue = Enumerable.Repeat((byte)0x80, 128).ToArray();

        [Fact]
        public void Cryptonote_SlowHash_Should_Match()
        {
            var result = LibCryptonote.CryptonightHashSlow(testValue).ToHexString();

            Assert.Equal(result, "9a267e32aefcc40ab12a906fd3f2de45a24a5ccde9e8b84528e656577f14e0fe");
        }

        [Fact]
        public void Cryptonote_SlowHash_Should_Throw_On_Null_Argument()
        {
            Assert.Throws<ArgumentNullException>(() => LibCryptonote.CryptonightHashSlow(null));
        }

        [Fact]
        public void Cryptonote_FastHash_Should_Match()
        {
            var result = LibCryptonote.CryptonightHashFast(testValue).ToHexString();

            Assert.Equal(result, "80ad002b1c333a29913d9edb5340412b121c0e9045e59fa9b2aabb53f9dcc92d");
        }

        [Fact]
        public void Cryptonote_FastHash_Should_Throw_On_Null_Argument()
        {
            Assert.Throws<ArgumentNullException>(() => LibCryptonote.CryptonightHashFast(null));
        }
    }
}
