namespace Miningcore.Blockchain.Ergo.Configuration
{
    public class ErgoPoolConfigExtra
    {
        /// <summary>
        /// Maximum number of tracked jobs.
        /// Default: 12 - you should increase this value if your blockrefreshinterval is higher than 300ms
        /// </summary>
        public int? MaxActiveJobs { get; set; }

        public int? MinimumConfirmations { get; set; }
    }
}
