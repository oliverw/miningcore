namespace Miningcore.Blockchain.Equihash;

public class EquihashExtraNonceProvider : ExtraNonceProviderBase
{
    public EquihashExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, 3, clusterInstanceId)
    {
    }
}
