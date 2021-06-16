using System;
using Miningcore.Extensions;
using Miningcore.Native;
using Xunit;

namespace Miningcore.Tests.Crypto
{
    public class LibRandomXTests : TestBase
    {
        const string realm = "xmr";
        const string seedHex = "7915d56de262bf23b1fb9104cf5d2a13fcbed2f6b4b9b657309c222b09f54bc0";
        private static readonly byte[] data = "0106a2aaafd505583cf50bcc743d04d831d2b119dc94ad88679e359076ee3f18d258ee138b3b42580100a4b1e2f4baf6ab7109071ab59bc52dba740d1de99fa0ae0c4afd6ea9f40c5d87ec01".HexToByteArray();
        private const string hashExpected = "55ef9dc0b8e0cb82c609a003c7d99504fc87f5e2dcd31f6fef318fc172cbc887";

        [Fact]
        public void CreateAndDeleteSeed()
        {
            // creation
            LibRandomX.CreateSeed(realm, seedHex);
            Assert.True(LibRandomX.realms.ContainsKey(realm));
            Assert.True(LibRandomX.realms[realm].ContainsKey(seedHex));

            // accessing the created seed should work
            Assert.NotNull(LibRandomX.GetSeed(realm, seedHex));

            // creating the same realm and key twice should not result in duplicates
            LibRandomX.CreateSeed(realm, seedHex);
            Assert.Equal(LibRandomX.realms.Count, 1);
            Assert.Equal(LibRandomX.realms[realm].Count, 1);

            // deletion
            LibRandomX.DeleteSeed(realm, seedHex);
            Assert.False(LibRandomX.realms[realm].ContainsKey(seedHex));
        }

        [Fact]
        public void CalculateHashSlow()
        {
            var buf = new byte[32];

            // light-mode
            LibRandomX.CreateSeed(realm, seedHex);

            LibRandomX.CalculateHash("xmr", seedHex, data, buf);
            var result = buf.ToHexString();
            Assert.Equal(hashExpected, result);

            Array.Clear(buf, 0, buf.Length);

            // second invocation should give the same result
            LibRandomX.CalculateHash("xmr", seedHex, data, buf);
            result = buf.ToHexString();
            Assert.Equal(hashExpected, result);

            LibRandomX.DeleteSeed(realm, seedHex);
        }

        [Fact]
        public void CalculateHashFast()
        {
            var buf = new byte[32];

            // fast-mode
            LibRandomX.CreateSeed(realm, seedHex, null, LibRandomX.randomx_flags.RANDOMX_FLAG_FULL_MEM);

            LibRandomX.CalculateHash("xmr", seedHex, data, buf);
            var result = buf.ToHexString();
            Assert.Equal(hashExpected, result);

            Array.Clear(buf, 0, buf.Length);

            // second invocation should give the same result
            LibRandomX.CalculateHash("xmr", seedHex, data, buf);
            result = buf.ToHexString();
            Assert.Equal(hashExpected, result);

            LibRandomX.DeleteSeed(realm, seedHex);
        }
    }
}
