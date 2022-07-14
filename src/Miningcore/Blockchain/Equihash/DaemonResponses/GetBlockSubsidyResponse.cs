namespace Miningcore.Blockchain.Equihash.DaemonResponses;

public class ZCashBlockSubsidy
{
    public decimal Miner { get; set; }
    public decimal? Founders { get; set; }
    public decimal? Community { get; set; }
    public decimal? Securenodes { get; set; }
    public decimal? Supernodes { get; set; }
    public List<FundingStream> FundingStreams { get; set; }
}

public class FundingStream
{
    public string Recipient { get; set; }
    public string Specification { get; set; }
    public decimal Value { get; set; }
    public decimal ValueZat { get; set; }
    public string Address { get; set; }
}
