/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

namespace Miningcore.Blockchain.Cryptonote.DaemonResponses
{

    /// <summary>
    /// submit_block<br />
    /// Alias: submitblock
    /// <para>Submit a mined block to the network.</para>
    /// <para>Inputs:
    /// <br>  Block blob data - array of strings - list of block blobs which have been mined. </br>
    /// <br>  See get_block_template to get a blob on which to mine.</br></para>
    /// </summary>
    public class SubmitResponse
    {
        /// <summary>
        ///  Outputs:
        ///  status - string - Block submit status.
        /// </summary>
        public string Status { get; set; }
    }
}
