using System.Threading.Tasks;
using AutoMapper;
using MiningForce.Blockchain.Daemon;
using MiningForce.Configuration;
using MiningForce.Extensions;
using MiningForce.Payments;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinPayoutHandler : PayoutHandlerBase,
		IPayoutHandler
	{
		public BitcoinPayoutHandler(IConnectionFactory cf, IMapper mapper,
			IShareRepository shares, IBlockRepository blocks, DaemonClient daemon) :
			base(cf, mapper, shares, blocks)
		{
			this.daemon = daemon;
		}

		private PoolConfig poolConfig;
		private readonly DaemonClient daemon;

		public void Configure(PoolConfig poolConfig)
		{
			this.poolConfig = poolConfig;

			daemon.Configure(poolConfig);
		}

		public Task<Block[]> GetConfirmedPendingBlocksAsync()
		{
			var pendingBlocks = cf.Run(con => blocks.GetPendingBlocksForPool(con, poolConfig.Id));

			return Task.FromResult(new Block[0]);
		}
	}
}
