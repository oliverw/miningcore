using Newtonsoft.Json;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class Block
{
    public uint Version { get; set; }
    public string Hash { get; set; }
    public string PreviousBlockhash { get; set; }
    public ulong Time { get; set; }
    public uint Height { get; set; }
    public string Bits { get; set; }
    public double Difficulty { get; set; }
    public string Nonce { get; set; }
    public uint Weight { get; set; }
    public uint Size { get; set; }
    public int Confirmations { get; set; }

    [JsonProperty("tx")]
    public string[] Transactions { get; set; }
}
