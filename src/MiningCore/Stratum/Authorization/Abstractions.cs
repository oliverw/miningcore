using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MiningCore.Blockchain;

namespace MiningCore.Stratum.Authorization
{
    public interface IStratumAuthorizer
    {
        Task<bool> AuthorizeAsync(IPEndPoint remotEndPoint, string username, string password, IBlockchainDemon blockchainDemon);
    }
}
