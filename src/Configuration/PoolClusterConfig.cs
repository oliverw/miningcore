using System.Collections.Generic;

namespace MiningForce.Configuration
{
    public enum HashAlgorithm
    {
        Sha256 = 1,
        Scrypt = 2,
    }

    public enum RewardRecipientType
    {
        Op,	// Pool operators
        Dev,
    }

    public enum StratumAuthorizerKind
    {
        AddressBased,
    }

    public class CoinConfig
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
        public HashAlgorithm Algorithm { get; set; }
    }

    public class NetworkEndpointConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public class AuthenticatedNetworkEndpointConfig : NetworkEndpointConfig
    {
        public string User { get; set; }
        public string Password { get; set; }
    }

    public class DatabaseEndpointConfig : AuthenticatedNetworkEndpointConfig
    {
        public string Database { get; set; }
    }

    public class PoolEndpoint
    {
        public string ListenAddress { get; set; }
        public double Difficulty { get; set; }
        public VarDiffConfig VarDiff { get; set; }
    }

    public class VarDiffConfig
    {
        /// <summary>
        /// Minimum difficulty
        /// </summary>
        public int MinDiff { get; set; }

        /// <summary>
        /// Network difficulty will be used if it is lower than this
        /// </summary>
        public int MaxDiff { get; set; }

        /// <summary>
        /// Try to get 1 share per this many seconds
        /// </summary>
        public int TargetTime { get; set; }

        /// <summary>
        /// Check to see if we should retarget every this many seconds
        /// </summary>
        public int RetargetTime { get; set; }

        /// <summary>
        /// Allow time to very this % from target without retargeting
        /// </summary>
        public int VariancePercent { get; set; }
    }

    public class BanningConfig
    {
        public bool Enabled { get; set; }
        public double Time { get; set; }   // How many seconds to ban worker for
        public double InvalidPercent { get; set; } // What percent of invalid shares triggers ban
        public double CheckThreshold { get; set; } // Check invalid percent when this many shares have been submitted
        public double PurgeInterval { get; set; } // Every this many seconds clear out the list of old bans
    }

    public class PaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public int PaymentInterval { get; set; }
        public double MinimumPayment { get; set; }
        public AuthenticatedNetworkEndpointConfig Daemon { get; set; }
    }

    public class RewardRecipientConfig
    {
        public RewardRecipientType Type { get; set; }
        public string Address { get; set; }
        public double Percentage { get; set; }
    }

    public class PoolConfig
    {
        public CoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public AuthenticatedNetworkEndpointConfig[] Daemons { get; set; }
        public PaymentProcessingConfig PaymentProcessing { get; set; }
        public BanningConfig Banning { get; set; }
        public DatabaseEndpointConfig Database { get; set; }
        public RewardRecipientConfig[] RewardRecipients { get; set; }
        public StratumAuthorizerKind Authorizer { get; set; }
        public string Address { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }
    }

    public class PoolClusterConfig
    {
        public PoolConfig[] Pools { get; set; }
    }
}
