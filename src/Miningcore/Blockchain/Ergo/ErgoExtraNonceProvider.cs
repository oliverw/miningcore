namespace Miningcore.Blockchain.Ergo;

public class ErgoExtraNonceProvider : ExtraNonceProviderBase
{
    public ErgoExtraNonceProvider(string poolId, int size, byte? clusterInstanceId) : base(poolId, size, clusterInstanceId)
    {
    }
}
