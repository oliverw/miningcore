using System.Data;

namespace MiningForce.Persistence.Repositories
{
    public interface IPaymentRepository
	{
		void Insert(IDbConnection con, IDbTransaction tx, Model.Payment payment);
	}
}
