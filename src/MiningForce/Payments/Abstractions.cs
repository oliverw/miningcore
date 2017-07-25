using System.Threading.Tasks;
using MiningForce.Configuration;
using MiningForce.Persistence.Model;

namespace MiningForce.Payments
{
	public interface IPayoutHandler
	{
		void Configure(PoolConfig poolConfig);

		Task<Block[]> ClassifyBlocksAsync(Block[] blocks);
		Task<double> GetNetworkDifficultyAsync();
		string FormatRewardAmount(double amount);
		Task PayoutAsync(Balance balance);
	}

	public interface IPayoutScheme
	{
		Task UpdateBalancesAndBlockAsync(PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block);
	}
}
