using System;
using System.Data;
using System.Threading.Tasks;

namespace Miningcore.Persistence.Dummy
{
    public class DummyConnectionFactory : IConnectionFactory
    {
        public DummyConnectionFactory(string connectionString)
        {
        }

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public Task<IDbConnection> OpenConnectionAsync()
        {
            throw new NotImplementedException();
        }
    }
}
