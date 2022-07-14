using Miningcore.Crypto;
using Miningcore.Extensions;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Miningcore.Tests.Crypto;

public class MerkleTreeTests : TestBase
{
    private readonly ITestOutputHelper output;

    public MerkleTreeTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void MerkleTree_SimpleTest_Branches()
    {
        var value1 = Encoding.ASCII.GetBytes("1");
        var value2 = Encoding.ASCII.GetBytes("2");

        var hashes = new List<byte[]>
        {
            MerkelHash(value1),
            MerkelHash(value2)
        };

        var tree = new MerkleTree(hashes);
        var output = tree.Branches;

        var expectedOutput = new[]
        {
            "9c2e4d8fe97d881430de4e754b4205b9c27ce96715231cffc4337340cb110280",
            "064a9b4691e6c7c53f8cbe713c02ce1cfd22ce261e6fdf042f4d4e1cdd547cdb"
        };

        Assert.Equal(expectedOutput, output);
    }

    [Fact]
    public void MerkleTree_100HashesTest_Branches()
    {
        var hashesList = new List<byte[]>();

        for(var i = 0; i < 100; i++)
        {
            var value = Encoding.ASCII.GetBytes(i.ToString());
            var hash = MerkelHash(value);

            hashesList.Add(hash);
        }

        var tree = new MerkleTree(hashesList);
        var output = tree.Branches;

        var expectedOutput = new[]
        {
            "67050eeb5f95abf57449d92629dcf69f80c26247e207ad006a862d1e4e6498ff",
            "7de236613dd3d9fa1d86054a84952f1e0df2f130546b394a4d4dd7b76997f607",
            "2501f389988298bebe860b37315788bdffc8b72533a95c820db9c8c40be4455f",
            "9d1f5c0296d5d8eee8ddf004d8b7ee14a8600ca881647ff8908c3ce5883cefb5",
            "fe59ea2519fc177d21280d49dc1b4278dc3e1d50d658fe92f5326e6c323ff9d8",
            "be1d18282617a6cdc27b71f9965221039edfb0437f8fae96345e944b02b67e19",
            "3c7e141f9a3816f2131d3248540701e09e69f50e34e6a77059e785ae3b8263c7"
        };

        Assert.Equal(expectedOutput, output);
    }

    [Fact]
    public void MerkleTree_99HashesTest_Branches()
    {
        var hashesList = new List<byte[]>();

        for(var i = 0; i < 100; i++)
        {
            var value = Encoding.ASCII.GetBytes(i.ToString());
            var hash = MerkelHash(value);
            hashesList.Add(hash);
        }

        var tree = new MerkleTree(hashesList);
        var output = tree.Steps
            .Select(x => x.ToHexString())
            .ToArray();

        foreach(var hex in output)
            this.output.WriteLine(hex);

        var expectedOutput = new[]
        {
            "67050eeb5f95abf57449d92629dcf69f80c26247e207ad006a862d1e4e6498ff",
            "7de236613dd3d9fa1d86054a84952f1e0df2f130546b394a4d4dd7b76997f607",
            "2501f389988298bebe860b37315788bdffc8b72533a95c820db9c8c40be4455f",
            "9d1f5c0296d5d8eee8ddf004d8b7ee14a8600ca881647ff8908c3ce5883cefb5",
            "fe59ea2519fc177d21280d49dc1b4278dc3e1d50d658fe92f5326e6c323ff9d8",
            "be1d18282617a6cdc27b71f9965221039edfb0437f8fae96345e944b02b67e19",
            "3c7e141f9a3816f2131d3248540701e09e69f50e34e6a77059e785ae3b8263c7"
        };

        Assert.Equal<string>(expectedOutput, output);
    }


    private static byte[] MerkelHash(byte[] input)
    {
        using(var hash = SHA256.Create())
        {
            var first = hash.ComputeHash(input, 0, input.Length);
            return hash.ComputeHash(first);
        }
    }
}
