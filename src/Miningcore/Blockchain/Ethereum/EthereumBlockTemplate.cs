namespace Miningcore.Blockchain.Ethereum;

public class EthereumBlockTemplate
{
    /// <summary>
    /// The block number
    /// </summary>
    public ulong Height { get; set; }

    /// <summary>
    /// current block header pow-hash (32 Bytes)
    /// </summary>
    public string Header { get; set; }

    /// <summary>
    /// the seed hash used for the DAG. (32 Bytes)
    /// </summary>
    public string Seed { get; set; }

    /// <summary>
    /// the boundary condition ("target"), 2^256 / difficulty. (32 Bytes)
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// integer of the difficulty for this block
    /// </summary>
    public ulong Difficulty { get; set; }
}
