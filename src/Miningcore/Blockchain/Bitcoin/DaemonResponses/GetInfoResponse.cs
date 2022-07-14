namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class DaemonInfo
{
    public string Version { get; set; }
    public int ProtocolVersion { get; set; }
    public int WalletVersion { get; set; }
    public decimal Balance { get; set; }
    public ulong Blocks { get; set; }
    public bool Testnet { get; set; }
    public int Connections { get; set; }
    public double Difficulty { get; set; }
}
