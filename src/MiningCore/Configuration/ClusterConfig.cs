/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiningCore.Configuration
{
    public enum CoinType
    {
        // ReSharper disable InconsistentNaming
        BTC = 1, // Bitcoin
        BCH, // Bitcoin Cash
        LTC, // Litecoin
        DOGE, // Dogecoin,
        XMR, // Monero
        GRS, // GroestlCoin
        DGB, // Digibyte
        NMC, // Namecoin
        VIA, // Viacoin
        PPC, // Peercoin
        ZEC, // ZCash
        ZCL, // ZClassic
        ZEN, // Zencash
        ETH, // Ethereum
        ETC, // Ethereum Classic
        EXP, // Expanse
        DASH, // Dash
        MONA, // Monacoin
        VTC, // Vertcoin
        BTG, // Bitcoin Gold
        GLT, // Globaltoken
        ELLA, // Ellaism
        AEON, // AEON
        STAK, // Straks
        ETN, // Electroneum
        MOON, // MoonCoin
        XVG,  // Verge
        GBX,  // GoByte
        CRC,  // CrowdCoin
        BTCP, // Bitcoin Private
        CLO,  // Callisto
        FLO, // Flo
        PAK, // PAKcoin
        CANN, // CannabisCoin
    }

    public class CoinConfig
    {
        public CoinType Type { get; set; }

        /// <summary>
        /// For coins like DGB which support multiple POW methods
        /// </summary>
        public string Algorithm { get; set; }
    }

    public enum PayoutScheme
    {
        // ReSharper disable once InconsistentNaming
        PPLNS = 1,
        Solo
    }

    public partial class ClusterLoggingConfig
    {
        public string Level { get; set; }
        public bool EnableConsoleLog { get; set; }
        public bool EnableConsoleColors { get; set; }
        public string LogFile { get; set; }
        public bool PerPoolLogFile { get; set; }
        public string LogBaseDirectory { get; set; }
    }

    public partial class NetworkEndpointConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
    }

    public partial class AuthenticatedNetworkEndpointConfig : NetworkEndpointConfig
    {
        public string User { get; set; }
        public string Password { get; set; }
    }

    public class DaemonEndpointConfig : AuthenticatedNetworkEndpointConfig
    {
        /// <summary>
        /// Use SSL to for RPC requests
        /// </summary>
        public bool Ssl { get; set; }

        /// <summary>
        /// Use HTTP2 protocol for RPC requests (don't use this unless your daemon(s) live behind a HTTP reverse proxy)
        /// </summary>
        public bool Http2 { get; set; }

        /// <summary>
        /// Validate SSL certificate (if SSL option is set to true)
        /// </summary>
        public bool ValidateCert { get; set; }

        /// <summary>
        /// Optional endpoint category
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Optional request path for RPC requests
        /// </summary>
        public string HttpPath { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public class DatabaseConfig : AuthenticatedNetworkEndpointConfig
    {
        public string Database { get; set; }
    }

    public class PoolEndpoint
    {
        public string ListenAddress { get; set; }
        public string Name { get; set; }
        public double Difficulty { get; set; }
        public VarDiffConfig VarDiff { get; set; }
    }

    public partial class VarDiffConfig
    {
        /// <summary>
        /// Minimum difficulty
        /// </summary>
        public double MinDiff { get; set; }

        /// <summary>
        /// Network difficulty will be used if it is lower than this
        /// </summary>
        public double? MaxDiff { get; set; }

        /// <summary>
        /// Do not alter difficulty by more than this during a single retarget in either direction
        /// </summary>
        public double? MaxDelta { get; set; }

        /// <summary>
        /// Try to get 1 share per this many seconds
        /// </summary>
        public double TargetTime { get; set; }

        /// <summary>
        /// Check to see if we should retarget every this many seconds
        /// </summary>
        public double RetargetTime { get; set; }

        /// <summary>
        /// Allow submission frequency to diverge this much (%) from target time without triggering a retarget
        /// </summary>
        public double VariancePercent { get; set; }
    }

    public enum BanManagerKind
    {
        Integrated = 1,
        IpTables
    }

    public class ClusterBanningConfig
    {
        public BanManagerKind? Manager { get; set; }

        /// <summary>
        /// Ban clients sending non-json or invalid json
        /// </summary>
        public bool? BanOnJunkReceive { get; set; }

        /// <summary>
        /// Ban miners for crossing invalid share threshold
        /// </summary>
        public bool? BanOnInvalidShares { get; set; }
    }

    public partial class PoolShareBasedBanningConfig
    {
        public bool Enabled { get; set; }
        public int CheckThreshold { get; set; } // Check stats when this many shares have been submitted
        public double InvalidPercent { get; set; } // What percent of invalid shares triggers ban
        public int Time { get; set; } // How many seconds to ban worker for
    }

    public partial class PoolPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public decimal MinimumPayment { get; set; } // in pool-base-currency (ie. Bitcoin, not Satoshis)
        public PayoutScheme PayoutScheme { get; set; }
        public JToken PayoutSchemeConfig { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class ClusterPaymentProcessingConfig
    {
        public bool Enabled { get; set; }
        public int Interval { get; set; }

        public string ShareRecoveryFile { get; set; }
    }

    public partial class PersistenceConfig
    {
        public DatabaseConfig Postgres { get; set; }
    }

    public class RewardRecipient
    {
        public string Address { get; set; }
        public decimal Percentage { get; set; }

        /// <summary>
        /// Optional recipient type
        /// </summary>
        public string Type { get; set; }
    }

    public partial class EmailSenderConfig : AuthenticatedNetworkEndpointConfig
    {
        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    public partial class AdminNotifications
    {
        public bool Enabled { get; set; }
        public string EmailAddress { get; set; }
        public bool NotifyBlockFound { get; set; }
        public bool NotifyPaymentSuccess { get; set; }
    }

    public partial class SlackNotifications
    {
        public bool Enabled { get; set; }
        public string WebHookUrl { get; set; }

        /// <summary>
        /// Optional default channel override - must start with '#'
        /// </summary>
        public string Channel { get; set; }

        public bool NotifyBlockFound { get; set; }
        public bool NotifyPaymentSuccess { get; set; }

        /// <summary>
        /// Override slack bot name for block found notifications - optional
        /// </summary>
        public string BlockFoundUsername { get; set; }

        /// <summary>
        /// Override slack bot name for payment notifications- optional
        /// </summary>
        public string PaymentSuccessUsername { get; set; }

        /// <summary>
        /// Override slack Emoji for block found notifications - optional
        /// </summary>
        public string BlockFoundEmoji { get; set; }

        /// <summary>
        /// Override slack Emoji for payment notifications- optional
        /// </summary>
        public string PaymentSuccessEmoji { get; set; }
    }

    public partial class NotificationsConfig
    {
        public bool Enabled { get; set; }

        public EmailSenderConfig Email { get; set; }
        public AdminNotifications Admin { get; set; }
    }

    public partial class ApiConfig
    {
        public bool Enabled { get; set; }
        public string ListenAddress { get; set; }
        public int Port { get; set; }
    }

    public partial class ZmqPubSubEndpointConfig
    {
        public string Url { get; set; }
        public string Topic { get; set; }
    }

    public partial class ShareRelayConfig
    {
        public string PublishUrl { get; set; }

        /// <summary>
        /// If set to true, the relay will "Connect" to the url, rather than "Bind" it 
        /// </summary>
        public bool Connect { get; set; }
    }

    public partial class PoolConfig
    {
        public string Id { get; set; }
        public string PoolName { get; set; }
        public bool Enabled { get; set; }
        public CoinConfig Coin { get; set; }
        public Dictionary<int, PoolEndpoint> Ports { get; set; }
        public DaemonEndpointConfig[] Daemons { get; set; }
        public PoolPaymentProcessingConfig PaymentProcessing { get; set; }
        public PoolShareBasedBanningConfig Banning { get; set; }
        public RewardRecipient[] RewardRecipients { get; set; }
        public SlackNotifications SlackNotifications { get; set; }
        public string Address { get; set; }
        public int ClientConnectionTimeout { get; set; }
        public int JobRebroadcastTimeout { get; set; }
        public int BlockRefreshInterval { get; set; }

        /// <summary>
        /// If true, internal stratum ports are not initialized
        /// </summary>
        public bool? EnableInternalStratum { get; set; }

        /// <summary>
        /// External stratums (ZMQ based share publishers)
        /// </summary>
        public ZmqPubSubEndpointConfig[] ExternalStratums { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }
    }

    public partial class ClusterConfig
    {
        public string ClusterName { get; set; }
        public ClusterLoggingConfig Logging { get; set; }
        public ClusterBanningConfig Banning { get; set; }
        public PersistenceConfig Persistence { get; set; }
        public ClusterPaymentProcessingConfig PaymentProcessing { get; set; }
        public NotificationsConfig Notifications { get; set; }
        public ApiConfig Api { get; set; }

        /// <summary>
        /// If this is enabled, shares are not written to the database
        /// but published on the specified ZeroMQ Url and using the
        /// poolid as topic
        /// </summary>
        public ShareRelayConfig ShareRelay { get; set; }

        /// <summary>
        /// Maximum parallelism of Equihash solver
        /// Increasing this value by one, increases pool peak memory consumption by 1 GB
        /// </summary>
        public int? EquihashMaxThreads { get; set; }

        public PoolConfig[] Pools { get; set; }
    }
}
