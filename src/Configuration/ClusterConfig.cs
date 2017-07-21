using System.Collections.Generic;

namespace MiningForce.Configuration
{
	public enum CoinType
	{
		Bitcoin = 1,
		Litecoin,
	}

	public class CoinConfig
	{
		public CoinType Type { get; set; }
	}

	public enum RewardRecipientType
	{
		Op, // Pool operators
		Dev,
	}

	public enum StratumAuthorizerKind
	{
		AddressBased,
	}

	public class ClusterLoggingConfig
	{
		public string Level { get; set; }
		public bool EnableConsoleLog { get; set; }
		public bool EnableConsoleColors { get; set; }
		public string LogFile { get; set; }
		//public bool PerPoolLogFile { get; set; }
		//public string LogBaseDirectory { get; set; }
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
		public double MinDiff { get; set; }

		/// <summary>
		/// Network difficulty will be used if it is lower than this
		/// </summary>
		public double MaxDiff { get; set; }

		/// <summary>
		/// Try to get 1 share per this many seconds
		/// </summary>
		public double TargetTime { get; set; }

		/// <summary>
		/// Check to see if we should retarget every this many seconds
		/// </summary>
		public double RetargetTime { get; set; }

		/// <summary>
		/// Allow time to very this % from target without retargeting
		/// </summary>
		public double VariancePercent { get; set; }
	}

	public enum BanManagerTypes
	{
		Integrated = 1,
		IpTables,
	}

	public class ClusterBanningConfig
	{
		public BanManagerTypes Manager { get; set; }
	}

	public class PoolBanningConfig
	{
		public bool Enabled { get; set; }
		public int CheckThreshold { get; set; } // Check stats when this many shares have been submitted
		public double InvalidPercent { get; set; } // What percent of invalid shares triggers ban
		public int Time { get; set; }   // How many seconds to ban worker for
	}

	public class PaymentProcessingConfig
	{
		public bool Enabled { get; set; }
		public int PaymentInterval { get; set; }
		public double MinimumPayment { get; set; }
		public AuthenticatedNetworkEndpointConfig Daemon { get; set; }
	}

	public class RewardRecipient
	{
		public RewardRecipientType Type { get; set; }
		public string Address { get; set; }
		public double Percentage { get; set; }
	}

	public class PoolConfig
	{
		public bool Enabled { get; set; }
		public CoinConfig Coin { get; set; }
		public Dictionary<int, PoolEndpoint> Ports { get; set; }
		public AuthenticatedNetworkEndpointConfig[] Daemons { get; set; }
		public PaymentProcessingConfig PaymentProcessing { get; set; }
		public PoolBanningConfig Banning { get; set; }
		public DatabaseEndpointConfig Database { get; set; }
		public RewardRecipient[] RewardRecipients { get; set; }
		public StratumAuthorizerKind Authorizer { get; set; }
		public string Address { get; set; }
		public int ClientConnectionTimeout { get; set; }
		public int JobRebroadcastTimeout { get; set; }
		public int BlockRefreshInterval { get; set; }
	}

	public class ClusterConfig
	{
		public ClusterLoggingConfig Logging { get; set; }
		public ClusterBanningConfig Banning { get; set; }
		public PoolConfig[] Pools { get; set; }
	}
}
