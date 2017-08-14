using System.Data;
using Npgsql;

namespace MiningCore.Persistence.Postgres
{
    public class ConnectionFactory : IConnectionFactory
    {
        private readonly string connectionString;

        public ConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        /// <summary>
        ///     This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public IDbConnection OpenConnection()
        {
            var con = new NpgsqlConnection(connectionString);
            con.Open();
            return con;
        }
    }
}
