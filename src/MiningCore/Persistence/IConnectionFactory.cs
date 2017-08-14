using System.Data;

namespace MiningCore.Persistence
{
    public interface IConnectionFactory
    {
        IDbConnection OpenConnection();
    }
}
