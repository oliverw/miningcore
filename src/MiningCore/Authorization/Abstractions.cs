using System.Net;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Authorization
{
    public interface IStratumAuthorizer
    {
        Task<bool> AuthorizeAsync(IPEndPoint remotEndPoint, string username, string password, IBlockchainDemon blockchainDemon);
    }
}
