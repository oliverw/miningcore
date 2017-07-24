using System.Threading.Tasks;
using MiningForce.Configuration;

namespace MiningForce.Payments
{
	public interface IPayoutHandler
	{
		void Configure(PoolConfig poolConfig);

		Task<Persistence.Model.Block[]> GetConfirmedPendingBlocksAsync();
	}

	public interface ISharePayoutDistributor
	{
	}
}
