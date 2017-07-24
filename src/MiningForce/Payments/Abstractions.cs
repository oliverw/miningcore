using System.Threading.Tasks;
using MiningForce.Configuration;
using Newtonsoft.Json.Linq;

namespace MiningForce.Payments
{
	public interface IPayoutHandler
	{
		void Configure(PoolConfig poolConfig);

		Task<Persistence.Model.Block[]> ClassifyBlocksAsync(Persistence.Model.Block[] blocks);
		Task<double> GetNetworkDifficultyAsync();
	}

	public interface IPayoutScheme
	{
		Task UpdateBalancesAndBlockAsync(JToken payoutConfig, IPayoutHandler payoutHandler, Persistence.Model.Block block);
	}
}
