using System.Data;

namespace MiningForce.Persistence.Repositories
{
    public interface IShareRepository
	{
		void Insert(IDbConnection con, IDbTransaction tx, Model.Share share);
	}
}
