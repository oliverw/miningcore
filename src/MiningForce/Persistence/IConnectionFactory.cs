using System.Data;
using System.Threading.Tasks;

namespace MiningForce.Persistence
{
    public interface IConnectionFactory
    {
        IDbConnection OpenConnection();
    }
}
