using System;
using System.Threading.Tasks;
using AutoMapper;
using CodeContracts;
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
			Contract.RequiresNonNull(cf, nameof(cf));
			Contract.RequiresNonNull(mapper, nameof(mapper));
			Contract.RequiresNonNull(shares, nameof(shares));
			Contract.RequiresNonNull(blocks, nameof(blocks));
			Contract.RequiresNonNull(daemon, nameof(daemon));

			this.daemon = daemon;
		}

		private PoolConfig poolConfig;
		private readonly DaemonClient daemon;

		public void Configure(PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

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
