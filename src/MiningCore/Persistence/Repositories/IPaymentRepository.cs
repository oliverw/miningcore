using System.Data;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IPaymentRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Payment payment);
    }
}
