namespace Miningcore.Blockchain.Bitcoin;

public class BitcoinStratumMethods
{
    /// <summary>
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string Subscribe = "mining.subscribe";

    /// <summary>
    /// Used to authorize a worker, required before any shares can be submitted.
    /// </summary>
    public const string Authorize = "mining.authorize";

    /// <summary>
    /// Basically the idea is that miner remember the last difficulty given by the previous mining session and it sends mining.suggest_difficulty(difficulty) on the beginning of the next session (it may be sent before mining.subscribe or mining.resume, but it should not be a requirement)
    /// </summary>
    public const string SuggestDifficulty = "mining.suggest_difficulty";

    /// <summary>
    /// Used to push new work to the miner.  Previous work should be aborted if Clean Jobs = true!
    /// </summary>
    public const string MiningNotify = "mining.notify";

    /// <summary>
    /// Used to submit shares
    /// </summary>
    public const string SubmitShare = "mining.submit";

    /// <summary>
    /// Used to signal the miner to stop submitting shares under the new difficulty.
    /// </summary>
    public const string SetDifficulty = "mining.set_difficulty";

    /// <summary>
    /// This call simply dumps transactions used for given job. Thanks to this, miners now have
    /// everything needed to reconstruct source block template used by the pool and they can
    /// check if pool isn't doing something nasty
    /// </summary>
    public const string GetTransactions = "mining.get_transactions";

    /// <summary>
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string ExtraNonceSubscribe = "mining.extranonce.subscribe";

    /// <summary>
    /// Appears to be a command sent by AntMiner devices for use with ASICBOOST.
    /// https://www.reddit.com/r/Bitcoin/comments/63yo27/some_circumstantial_evidence_supporting_the_claim/dfy5o65/
    /// </summary>
    public const string MiningMultiVersion = "mining.multi_version";

    /// <summary>
    /// The client uses the message to advertise its features and to request/allow some protocol extensions.
    /// https://github.com/slushpool/stratumprotocol/blob/master/stratum-extensions.mediawiki#Request_miningconfigure
    /// </summary>
    public const string MiningConfigure = "mining.configure";
}
