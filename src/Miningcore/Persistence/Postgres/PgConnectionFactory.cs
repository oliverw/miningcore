using System.Data;
using Npgsql;

namespace Miningcore.Persistence.Postgres;

public class PgConnectionFactory : IConnectionFactory
{
    public PgConnectionFactory(string connectionString)
    {
        this.connectionString = connectionString;
    }

    private readonly string connectionString;

    public async Task<IDbConnection> OpenConnectionAsync()
    {
        var con = new NpgsqlConnection(connectionString);
        await con.OpenAsync();
        return con;
    }
}
