using System.Data;
using MiningForce.Blockchain;
using MiningForce.Configuration;

namespace MiningForce.Persistence.Repositories
{
    public interface IBlockRepository
    {
	    void Insert(IDbConnection con, IDbTransaction tx, Model.Block block);

	    Model.Block[] GetPendingBlocksForPool(IDbConnection con, string poolid);
	}
}
