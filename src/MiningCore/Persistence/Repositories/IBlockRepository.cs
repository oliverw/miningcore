using System.Data;
using MiningCore.Persistence.Model;

namespace MiningCore.Persistence.Repositories
{
    public interface IBlockRepository
    {
        void Insert(IDbConnection con, IDbTransaction tx, Block block);
        void DeleteBlock(IDbConnection con, IDbTransaction tx, Block block);

        void UpdateBlock(IDbConnection con, IDbTransaction tx, Block block);
        Block[] GetPendingBlocksForPool(IDbConnection con, string poolid);
    }
}
