using System.Net;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Authorization
{
    public class AddressBasedAuthorizer : IStratumAuthorizer
    {
        public Task<bool> AuthorizeAsync(IPEndPoint remotEndPoint, string username, string password, IBlockchainDemon blockchainDemon)
        {
            return blockchainDemon.ValidateAddressAsync(username);
        }
    }
}
