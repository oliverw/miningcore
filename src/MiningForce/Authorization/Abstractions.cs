using System.Net;
using System.Threading.Tasks;
using MiningForce.Blockchain;

namespace MiningForce.Authorization
{
    public interface IWorkerAuthorizer
    {
        Task<bool> AuthorizeAsync(IBlockchainJobManager manager, IPEndPoint remotEndPoint, string username, string password);
    }
}
