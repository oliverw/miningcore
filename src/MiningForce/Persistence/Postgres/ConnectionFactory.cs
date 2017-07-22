using System.Data;
using NLog;
using Npgsql;

namespace MiningForce.Persistence.Postgres
{
    public class ConnectionFactory
    {
        public ConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private readonly string connectionString;

        /// <summary>
        /// This implementation ensures that Glimpse.ADO is able to collect data
        /// </summary>
        /// <returns></returns>
        public IDbConnection GetConnection()
        {
            var con = new NpgsqlConnection(connectionString);
            con.Open();
            return con;
        }
    }
}
