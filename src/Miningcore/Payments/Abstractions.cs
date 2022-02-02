using System.Data;
using Miningcore.Configuration;
using Miningcore.Mining;
using Miningcore.Persistence.Model;

namespace Miningcore.Payments;

public interface IPayoutHandler
{
    Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct);

    Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct);
    Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct);
    Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct);
    Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct);
    Task<PayoutReceipt> PayoutAsync(Balance balance);
    void ConfigureOnDemandPayoutAsync(CancellationToken ct);
    Task<decimal> GetWalletBalance();
    decimal GetTransactionDeduction(decimal amount);
    bool MinersPayTxFees();
    double AdjustShareDifficulty(double difficulty);
    
    string FormatAmount(decimal amount);
}

public interface IPayoutScheme
{
    Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, IPayoutHandler payoutHandler, Block block, decimal blockReward, CancellationToken ct);
}
