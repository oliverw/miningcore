using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Miningcore.Blockchain.Pandanite
{
    public interface IPandaniteNodeApi
    {
        Task<(bool success, uint block)> GetBlock();
        Task<(bool success, MiningProblem data)> GetMiningProblem();
        Task<(bool success, List<Transaction> data)> GetTransactions();
        Task<bool> Submit(Stream stream);
        Task<(bool success, Dictionary<string, string> data)> VerifyTransactions(string[] txs);
        Task<(bool success, List<TransactionStatus> data)> SubmitTransactions(List<Transaction> transactions);
        Task<(bool success, ulong hashrate)> GetNetworkHashrate();
        Task<(bool success, List<string> peers)> GetPeers();
    }
}