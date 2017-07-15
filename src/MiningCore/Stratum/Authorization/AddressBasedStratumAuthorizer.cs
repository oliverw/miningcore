using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Stratum.Authorization
{
    public class AddressBasedStratumAuthorizer : IStratumAuthorizer
    {
        public Task<bool> AuthorizeAsync(IPEndPoint remotEndPoint, string username, string password, IBlockchainDemon blockchainDemon)
        {
            return blockchainDemon.ValidateAddressAsync(username);
        }
    }
}
