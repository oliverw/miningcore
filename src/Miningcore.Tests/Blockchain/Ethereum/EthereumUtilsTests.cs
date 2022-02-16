using System.Numerics;
using Miningcore.Blockchain.Ethereum;
using Xunit;

namespace Miningcore.Tests.Blockchain.Ethereum
{
    public class EthereumUtilsTests : TestBase
    {

        [Fact]
        public void DetectNetworkAndChain_Hex_WithPrefix()
        {
            EthereumUtils.DetectNetworkAndChain("1", "ethereum classic", "0x3",
                out EthereumNetworkType ethereumNetworkType, out GethChainType gethChainType, out BigInteger chainId);

            Assert.Equal(EthereumNetworkType.Mainnet, ethereumNetworkType);
            Assert.Equal(GethChainType.Ethereum, gethChainType);
            Assert.Equal(3, chainId);
        }

        [Fact]
        public void DetectNetworkAndChain_Hex_Prefix_UpperCase()
        {
            EthereumUtils.DetectNetworkAndChain("1", "ethereum classic", "0X2A",
                out EthereumNetworkType ethereumNetworkType, out GethChainType gethChainType, out BigInteger chainId);
            
            Assert.Equal(EthereumNetworkType.Mainnet, ethereumNetworkType);
            Assert.Equal(GethChainType.Ethereum, gethChainType);
            Assert.Equal(42, chainId);

            EthereumUtils.DetectNetworkAndChain("1", "ethereum classic", "0X3D", out ethereumNetworkType, out gethChainType, out chainId);
            Assert.Equal(61, chainId);
        }

        [Fact]
        public void DetectNetworkAndChain_Hex_WithoutPrefix()
        {
            EthereumUtils.DetectNetworkAndChain("1", "ethereum classic", "03",
                out EthereumNetworkType ethereumNetworkType, out GethChainType gethChainType, out BigInteger chainId);

            Assert.Equal(EthereumNetworkType.Mainnet, ethereumNetworkType);
            Assert.Equal(GethChainType.Ethereum, gethChainType);
            Assert.Equal(3, chainId);
        }

        [Fact]
        public void DetectNetworkAndChain_Number()
        {
            EthereumUtils.DetectNetworkAndChain("1", "ethereum classic", "3",
                out EthereumNetworkType ethereumNetworkType, out GethChainType gethChainType, out BigInteger chainId);

            Assert.Equal(EthereumNetworkType.Mainnet, ethereumNetworkType);
            Assert.Equal(GethChainType.Ethereum, gethChainType);
            Assert.Equal(3, chainId);
        }
    }
}
