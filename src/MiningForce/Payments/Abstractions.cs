using System.Threading.Tasks;
using MiningForce.Configuration;
using MiningForce.Persistence.Model;

namespace MiningForce.Payments
{
	public interface IPayoutHandler
	{
		void Configure(PoolConfig poolConfig);

		Task<Block[]> ClassifyBlocksAsync(Block[] blocks);
		Task PayoutAsync(Balance[] balances);

		string FormatRewardAmount(decimal amount);
	}

	public interface IPayoutScheme
	{
		Task UpdateBalancesAndBlockAsync(PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block);
	}
}
