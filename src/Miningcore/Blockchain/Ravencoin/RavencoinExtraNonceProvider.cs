namespace Miningcore.Blockchain.Ravencoin;

public class RavencoinExtraNonceProvider : ExtraNonceProviderBase
{
    public RavencoinExtraNonceProvider(string poolId, byte? clusterInstanceId) : base(poolId, RavencoinConstants.ExtranoncePlaceHolderLength, clusterInstanceId)
    {
    }
}
