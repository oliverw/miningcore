using System.Collections.Generic;

namespace MiningCore.Configuration
{
    public enum HashAlgorithm
    {
        Sha256 = 1,
        Scrypt = 2,
    }

    public enum RewardRecipientType
    {
        Pool,
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
        public float Difficulty { get; set; }
        public VarDiffConfig VarDiff { get; set; }
    }

    public class VarDiffConfig
    {
        public double MinDiff { get; set; }
        public double MaxDiff { get; set; }
        public double TargetTime { get; set; }
        public double RetargetTime { get; set; }
        public double VariancePercent { get; set; }
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
