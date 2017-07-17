using System.Net;
using System.Threading.Tasks;
using MiningForce.Blockchain;

namespace MiningForce.Authorization
{
    public class AddressBasedWorkerAuthorizer : IWorkerAuthorizer
    {
        public Task<bool> AuthorizeAsync(IBlockchainJobManager manager, IPEndPoint remotEndPoint, string username, string password)
        {
            return manager.ValidateAddressAsync(username);
        }
    }
}
