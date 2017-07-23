using System.Data;
using MiningForce.Blockchain;

namespace MiningForce.Persistence.Repositories
{
    public interface IBlockRepository
    {
	    void Insert(IDbConnection con, IDbTransaction tx, Model.Block block);
    }
}
