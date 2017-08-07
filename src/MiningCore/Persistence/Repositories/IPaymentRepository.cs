using System.Data;

namespace MiningCore.Persistence.Repositories
{
    public interface IPaymentRepository
	{
		void Insert(IDbConnection con, IDbTransaction tx, Model.Payment payment);
	}
}
