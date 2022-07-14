using System.Data;

namespace Miningcore.Persistence;

public interface IConnectionFactory
{
    Task<IDbConnection> OpenConnectionAsync();
}
