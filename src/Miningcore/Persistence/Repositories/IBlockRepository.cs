using System.Data;
using Miningcore.Persistence.Model;

namespace Miningcore.Persistence.Repositories;

public interface IBlockRepository
{
    Task InsertAsync(IDbConnection con, IDbTransaction tx, Block block);
    Task DeleteBlockAsync(IDbConnection con, IDbTransaction tx, Block block);
    Task UpdateBlockAsync(IDbConnection con, IDbTransaction tx, Block block);

    Task<Block[]> PageBlocksAsync(IDbConnection con, string poolId, BlockStatus[] status, int page, int pageSize, CancellationToken ct);
    Task<Block[]> PageBlocksAsync(IDbConnection con, BlockStatus[] status, int page, int pageSize, CancellationToken ct);
    Task<Block[]> GetPendingBlocksForPoolAsync(IDbConnection con, string poolId);
    Task<Block> GetBlockBeforeAsync(IDbConnection con, string poolId, BlockStatus[] status, DateTime before);
    Task<uint> GetPoolBlockCountAsync(IDbConnection con, string poolId, CancellationToken ct);
    Task<DateTime?> GetLastPoolBlockTimeAsync(IDbConnection con, string poolId);
    Task<Block> GetBlockByHeightAsync(IDbConnection con, string poolId, long height);
}
