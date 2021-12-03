namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class PeerInfo
{
    public int Id { get; set; }
    public string Addr { get; set; }
    public int Version { get; set; }
    public string SubVer { get; set; }
    public int Blocks { get; set; }
    public int StartingHeight { get; set; }
    public int TimeOffset { get; set; }
    public double BanScore { get; set; }
    public int ConnTime { get; set; }
}
