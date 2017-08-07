using System.Data;

namespace MiningCore.Persistence.Repositories
{
    public interface IBlockRepository
    {
	    void Insert(IDbConnection con, IDbTransaction tx, Model.Block block);
	    void DeleteBlock(IDbConnection con, IDbTransaction tx, Model.Block block);

		void UpdateBlock(IDbConnection con, IDbTransaction tx, Model.Block block);
		Model.Block[] GetPendingBlocksForPool(IDbConnection con, string poolid);
    }
}
