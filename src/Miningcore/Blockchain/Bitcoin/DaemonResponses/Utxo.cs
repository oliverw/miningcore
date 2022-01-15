namespace Miningcore.Blockchain.Bitcoin.DaemonResponses;

public class Utxo
{
    public string TxId { get; set; }
    public int Vout { get; set; }
    public string Address { get; set; }
    public decimal Amount { get; set; }
    public string ScriptPubKey { get; set; }
    public int Confirmations { get; set; }
    public bool Spendable { get; set; }
}
