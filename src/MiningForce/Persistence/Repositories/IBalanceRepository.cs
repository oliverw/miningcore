using System.Data;

namespace MiningForce.Persistence.Repositories
{
    public interface IBalanceRepository
	{
		void InsertOrUpdate(IDbConnection con, IDbTransaction tx, Model.Balance share);
	}
}
