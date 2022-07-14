namespace Miningcore.Blockchain.Ethereum;

public class EthereumExtraNonceProvider : ExtraNonceProviderBase
{
    public EthereumExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, 2, clusterInstanceId)
    {
    }
}
