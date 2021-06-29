using System.Data;
using System.Threading.Tasks;

namespace Miningcore.Persistence
{
    public interface IConnectionFactory
    {
        Task<IDbConnection> OpenConnectionAsync();
    }
}
