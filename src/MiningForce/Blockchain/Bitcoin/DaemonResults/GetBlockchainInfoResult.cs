namespace MiningForce.Blockchain.Bitcoin.DaemonResults
{
    public class GetBlockchainInfoResult
    {
        public string Chain { get; set; }
        public int Blocks { get; set; }
        public int Headers { get; set; }
        public string BestBlockHash { get; set; }
        public double Difficulty { get; set; }
        public long MedianTime { get; set; }
        public double VerificationProgress { get; set; }
        public bool Pruned { get; set; }
    }
}
