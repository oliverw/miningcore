using System.Net;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Authorization
{
    public interface IWorkerAuthorizer
    {
        Task<bool> AuthorizeAsync(IBlockchainJobManager manager, IPEndPoint remotEndPoint, string username, string password);
    }
}
