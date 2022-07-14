namespace Miningcore.Blockchain;

public class BlockchainStats
{
    public string NetworkType { get; set; }
    public double NetworkHashrate { get; set; }
    public double NetworkDifficulty { get; set; }
    public string NextNetworkTarget { get; set; }
    public string NextNetworkBits { get; set; }
    public DateTime? LastNetworkBlockTime { get; set; }
    public ulong BlockHeight { get; set; }
    public int ConnectedPeers { get; set; }
    public string RewardType { get; set; }
}

public interface IExtraNonceProvider
{
    int ByteSize { get; }
    string Next();
}
