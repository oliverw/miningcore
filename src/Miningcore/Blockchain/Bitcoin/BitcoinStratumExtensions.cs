namespace Miningcore.Blockchain.Bitcoin;

/// <summary>
/// The client uses the message to advertise its features and to request/allow some protocol extensions.
/// https://github.com/slushpool/stratumprotocol/blob/master/stratum-extensions.mediawiki#Request_miningconfigure
/// </summary>
public class BitcoinStratumExtensions
{
    public const string VersionRolling = "version-rolling";
    public const string MinimumDiff = "minimum-difficulty";
    public const string SubscribeExtranonce = "subscribe-extranonce";

    public const string VersionRollingMask = VersionRolling + "." + "mask";
    public const string VersionRollingBits = VersionRolling + "." + "min-bit-count";

    public const string MinimumDiffValue = MinimumDiff + "." + "value";
}
