using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MiningForce.Configuration
{
	public enum CoinType
	{
		BTC = 1,	// Bitcoin
		LTC,		// Litecoin
		DOGE,		// Dogecoin
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

	public enum PayoutScheme
	{
		// ReSharper disable once InconsistentNaming
		PPLNS = 1
	}

	public class ClusterLoggingConfig
	{
		public string Level { get; set; }
		public bool EnableConsoleLog { get; set; }
		public bool EnableConsoleColors { get; set; }
		public string LogFile { get; set; }
		public bool PerPoolLogFile { get; set; }
		public string LogBaseDirectory { get; set; }
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

	public class DatabaseConfig : AuthenticatedNetworkEndpointConfig
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

	public enum BanManagerKind
	{
		Integrated = 1,
		IpTables,
	}

	public class ClusterBanningConfig
	{
		public BanManagerKind Manager { get; set; }
	}

	public class PoolBanningConfig
	{
		public bool Enabled { get; set; }
		public int CheckThreshold { get; set; } // Check stats when this many shares have been submitted
		public double InvalidPercent { get; set; } // What percent of invalid shares triggers ban
		public int Time { get; set; }   // How many seconds to ban worker for
	}

	public class PoolPaymentProcessingConfig
	{
		public bool Enabled { get; set; }
		public decimal MinimumPayment { get; set; }  // in pool-base-currency (ie. Bitcoin, not Satoshis)
		public PayoutScheme PayoutScheme { get; set; }
		public JToken PayoutSchemeConfig { get; set; }
	}

	public class ClusterPaymentProcessingConfig
	{
		public bool Enabled { get; set; }
		public int Interval { get; set; }

		public string ShareRecoveryFile { get; set; }
	}

	public class PersistenceConfig
	{
		public DatabaseConfig Postgres { get; set; }
	}

	public class RewardRecipient
	{
		public RewardRecipientType Type { get; set; }
		public string Address { get; set; }
		public decimal Percentage { get; set; }
	}

	public class PoolConfig
	{
		public string Id { get; set; }
		public bool Enabled { get; set; }
		public CoinConfig Coin { get; set; }
		public Dictionary<int, PoolEndpoint> Ports { get; set; }
		public AuthenticatedNetworkEndpointConfig[] Daemons { get; set; }
		public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
		public PoolBanningConfig Banning { get; set; }
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
		public PersistenceConfig Persistence { get; set; }
		public ClusterPaymentProcessingConfig PaymentProcessing { get; set; }
		public bool DisableDevDonation { get; set; }

		public PoolConfig[] Pools { get; set; }
	}
}
