using System.Data;
using System.Threading.Tasks;
using Miningcore.Persistence;

namespace Miningcore.Tests.Persistence.Postgres
{
    public class MockConnectionFactory : IConnectionFactory
    {
        private readonly IDbConnection dbCon;
        public MockConnectionFactory(IDbConnection con)
        {
            dbCon = con;
        }

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public Task<IDbConnection> OpenConnectionAsync()
        {
            return Task.FromResult(dbCon);
        }
    }
}
