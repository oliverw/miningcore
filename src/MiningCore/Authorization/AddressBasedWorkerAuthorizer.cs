using System.Net;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Authorization
{
    public class AddressBasedWorkerAuthorizer : IWorkerAuthorizer
    {
        public Task<bool> AuthorizeAsync(IBlockchainJobManager manager, IPEndPoint remotEndPoint, string username, string password)
        {
            return manager.ValidateAddressAsync(username);
        }
    }
}
