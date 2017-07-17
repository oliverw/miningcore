namespace MiningForce.Blockchain.Bitcoin.Commands
{
    public class BitcoinBlockTransaction
    {
        public string Data { get; set; }
        public string TxId { get; set; }
        public string Hash { get; set; }
    }

    public class GetBlockTemplateResponse
    {
        public int Version { get; set; }
        public string PreviousBlockhash { get; set; }
        public double CoinbaseValue { get; set; }
        public string Target { get; set; }
        public string NonceRange { get; set; }
        public uint Curtime { get; set; }
        public string Bits { get; set; }
        public uint Height { get; set; }
        public BitcoinBlockTransaction[] Transactions { get; set; }
    }
}
