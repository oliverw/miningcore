namespace Miningcore.Blockchain.Ethereum;

public static class EthereumStratumMethods
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
    /// Used to subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string ExtraNonceSubscribe = "mining.extranonce.subscribe";

    /// <summary>
    /// Used to login & subscribe to work from a server, required before all other communication.
    /// </summary>
    public const string SubmitLogin = "eth_submitLogin";

    /// <summary>
    /// Used to request work
    /// </summary>
    public const string GetWork = "eth_getWork";

    /// <summary>
    /// Used to submit work (shares)
    /// </summary>
    public const string SubmitWork = "eth_submitWork";

    /// <summary>
    /// Ignored
    /// </summary>
    public const string SubmitHashrate = "eth_submitHashrate";
}
