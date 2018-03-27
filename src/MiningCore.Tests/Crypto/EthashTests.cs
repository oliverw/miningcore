using System;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Crypto.Hashing.Algorithms;
using MiningCore.Crypto.Hashing.Ethash;
using MiningCore.Extensions;
using NLog;
using Xunit;

namespace MiningCore.Tests.Crypto
{
    public class EthashTests : TestBase
    {
        private ILogger logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task Ethhash_Verify_Valid_Blocks()
        {
            var validBlocks = new[] {
                new Block
                {
                    // from proof of concept nine testnet, epoch 0
                    Height =     22,
                    HashNoNonce ="372eca2454ead349c3df0ab5d00b0b706b23e49d469387db91811cee0358fc6d".HexToByteArray(),
                    Difficulty = new BigInteger(132416),
                    Nonce =      0x495732e0ed7a801c,
                    MixDigest =  "2f74cdeb198af0b9abe65d22d372e22fb2d474371774a9583c1cc427a07939f5".HexToByteArray(),
                },
                // from proof of concept nine testnet, epoch 1
                new Block
                {
                    Height =      30001,
                    HashNoNonce = "7e44356ee3441623bc72a683fd3708fdf75e971bbe294f33e539eedad4b92b34".HexToByteArray(),
                    Difficulty =  new BigInteger(1532671),
                    Nonce =       0x318df1c8adef7e5e,
                    MixDigest =   "144b180aad09ae3c81fb07be92c8e6351b5646dda80e6844ae1b697e55ddde84".HexToByteArray(),
                },
                // from proof of concept nine testnet, epoch 2
                new Block
                {
                    Height      = 60000,
                    HashNoNonce = "5fc898f16035bf5ac9c6d9077ae1e3d5fc1ecc3c9fd5bee8bb00e810fdacbaa0".HexToByteArray(),
                    Difficulty =  new BigInteger(2467358),
                    Nonce =       0x50377003e5d830ca,
                    MixDigest =   "ab546a5b73c452ae86dadd36f0ed83a6745226717d3798832d1b20b489e82063".HexToByteArray(),
                },
            };

            using (var ethash = new EthashLight(3))
            {
                Assert.True(await ethash.VerifyBlockAsync(validBlocks[0], logger));
                Assert.True(await ethash.VerifyBlockAsync(validBlocks[1], logger));
                Assert.True(await ethash.VerifyBlockAsync(validBlocks[2], logger));
            }
        }

        [Fact]
        public async Task Ethhash_Verify_Invalid_Blocks()
        {
            var hasher = new Sha3_256();

            var invalidBlocks = new[] {
                // totally nonsense block
                new Block
                {
                    Height = 61440000,
                    HashNoNonce = hasher.Digest(Encoding.UTF8.GetBytes("foo")),
                    Difficulty = new BigInteger(0),
                    Nonce = 0xcafebabec00000fe,
                    MixDigest = hasher.Digest(Encoding.UTF8.GetBytes("bar")),
                },
                new Block
                {
                    // from proof of concept nine testnet, epoch 0 - altered Nonce
                    Height =     22,
                    HashNoNonce ="372eca2454ead349c3df0ab5d00b0b706b23e49d469387db91811cee0358fc6d".HexToByteArray(),
                    Difficulty = new BigInteger(132416),
                    Nonce =      0x495732e0ed7a801d,
                    MixDigest =  "2f74cdeb198af0b9abe65d22d372e22fb2d474371774a9583c1cc427a07939f5".HexToByteArray(),
                },
                new Block
                {
                    // from proof of concept nine testnet, epoch 0 - altered HashNoNonce
                    Height =     22,
                    HashNoNonce ="472eca2454ead349c3df0ab5d00b0b706b23e49d469387db91811cee0358fc6d".HexToByteArray(),
                    Difficulty = new BigInteger(132416),
                    Nonce =      0x495732e0ed7a801c,
                    MixDigest =  "2f74cdeb198af0b9abe65d22d372e22fb2d474371774a9583c1cc427a07939f5".HexToByteArray(),
                },
                new Block
                {
                    // from proof of concept nine testnet, epoch 0 - altered MixDigest
                    Height =     22,
                    HashNoNonce ="372eca2454ead349c3df0ab5d00b0b706b23e49d469387db91811cee0358fc6d".HexToByteArray(),
                    Difficulty = new BigInteger(132416),
                    Nonce =      0x495732e0ed7a801c,
                    MixDigest =  "3f74cdeb198af0b9abe65d22d372e22fb2d474371774a9583c1cc427a07939f5".HexToByteArray(),
                },
            };

            using (var ethash = new EthashLight(3))
            {
                Assert.False(await ethash.VerifyBlockAsync(invalidBlocks[0], logger));
                Assert.False(await ethash.VerifyBlockAsync(invalidBlocks[1], logger));
                Assert.False(await ethash.VerifyBlockAsync(invalidBlocks[2], logger));
                Assert.False(await ethash.VerifyBlockAsync(invalidBlocks[3], logger));
            }
        }

        [Fact]
        public async Task EthHash_VerifyAsync_Should_Throw_On_Null_Argument()
        {
            using (var ethash = new EthashLight(3))
            {
                await Assert.ThrowsAsync<ArgumentNullException>(async () => await ethash.VerifyBlockAsync(null, logger));
            }
        }
    }
}
