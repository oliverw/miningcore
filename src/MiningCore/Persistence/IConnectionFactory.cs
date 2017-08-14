using System.Data;
using System.Threading.Tasks;

namespace MiningCore.Persistence
{
    public interface IConnectionFactory
    {
        IDbConnection OpenConnection();
    }
}