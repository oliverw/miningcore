namespace Miningcore.Blockchain.Equihash;

public class EquihashStratumMethods
{
    /// <summary>
    /// Used to signal the miner to stop submitting shares under the new target
    /// </summary>
    public const string SetTarget = "mining.set_target";

    /// <summary>
    /// The target suggested by the miner for the next received job and all subsequent jobs (until the next time this message is sent)
    /// </summary>
    public const string SuggestTarget = "mining.suggest_target";
}
