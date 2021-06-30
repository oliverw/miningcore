using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace Miningcore.Persistence.Postgres
{
    public class PgConnectionFactory : IConnectionFactory
    {
        public PgConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private readonly string connectionString;

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public async Task<IDbConnection> OpenConnectionAsync()
        {
            var con = new NpgsqlConnection(connectionString);
            await con.OpenAsync();
            return con;
        }
    }
}
