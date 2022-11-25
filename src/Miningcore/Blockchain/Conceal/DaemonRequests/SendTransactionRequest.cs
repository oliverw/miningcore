using Newtonsoft.Json;

namespace Miningcore.Blockchain.Conceal.DaemonRequests;

public class SendTransactionTransfers
{
    [JsonProperty("address")]
    public string Address { get; set; }
    
    [JsonProperty("amount")]
    public ulong Amount { get; set; }
}

public class SendTransactionRequest
{
    /// <summary>
    /// Privacy level (a discrete number from 1 to infinity). Level 5 is recommended
    /// </summary>
    [JsonProperty("anonymity")]
    public uint Anonymity { get; set; } = 5;
    
    /// <summary>
    /// Transaction fee. The fee in Conceal is fixed at .001 CCX. This parameter should be specified in minimal available CCX units. For example, if your fee is .001 CCX, you should pass it as 1000
    /// </summary>
    [JsonProperty("fee")]
    public ulong Fee { get; set; } = 1000;
    
    /// <summary>
    /// (Optional) Height of the block until which transaction is going to be locked for spending (0 to not add a lock)
    /// </summary>
    [JsonProperty("unlockTime")]
    public uint UnlockTime { get; set; } = 0;
    
    /// <summary>
    /// (Optional) Array of strings, where each string is an address to take the funds from
    /// </summary>
    [JsonProperty("addresses")]
    public string[] Addresses { get; set; }
    
    /// <summary>
    /// Array of strings and integers, where each string and integer are respectively an address and an amount where the funds are going to
    /// </summary>
    [JsonProperty("transfers")]
    public SendTransactionTransfers[] Transfers { get; set; }
    
    /// <summary>
    /// (Optional) Valid and existing address in the conceal wallet container used by walletd (Conceal Wallet RPC), it will receive the change of the transaction
    /// IMPORTANT RULES:
    /// 1: if container contains only 1 address, changeAddress field can be left empty and the change is going to be sent to this address
    /// 2: if addresses field contains only 1 address, changeAddress can be left empty and the change is going to be sent to this address
    /// 3: in the rest of the cases, changeAddress field is mandatory and must contain an address.
    /// </summary>
    [JsonProperty("changeAddress")]
    public string ChangeAddress { get; set; }
}