namespace Miningcore.Blockchain.Ethereum;

public record EthereumBlockTemplate
{
    /// <summary>
    /// The block number
    /// </summary>
    public ulong Height { get; init; }

    /// <summary>
    /// current block header pow-hash (32 Bytes)
    /// </summary>
    public string Header { get; init; }

    /// <summary>
    /// the seed hash used for the DAG. (32 Bytes)
    /// </summary>
    public string Seed { get; init; }

    /// <summary>
    /// the boundary condition ("target"), 2^256 / difficulty. (32 Bytes)
    /// </summary>
    public string Target { get; init; }

    /// <summary>
    /// integer of the difficulty for this block
    /// </summary>
    public ulong Difficulty { get; init; }
}
