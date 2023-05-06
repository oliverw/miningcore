namespace Miningcore.Blockchain.Pandanite
{
    public class MiningProblem
    {
        public uint chainLength { get; set; }
        public uint challengeSize { get; set; }
        public string lastHash { get; set; }
        public string lastTimestamp { get; set; }
        public ulong miningFee { get; set; }
    }
}
