using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CodeContracts;
using MiningForce.Configuration;
using MiningForce.Daemon;
using MiningForce.Payments;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;
using MiningForce.Util;
using MDC = MiningForce.Blockchain.Monero.MoneroDaemonCommands;

namespace MiningForce.Blockchain.Monero
{
    public class MoneroPayoutHandler : PayoutHandlerBase,
		IPayoutHandler
	{
		public MoneroPayoutHandler(IConnectionFactory cf, IMapper mapper,
			DaemonClient daemon,
			IShareRepository shareRepo, 
			IBlockRepository blockRepo,
			IBalanceRepository balanceRepo,
			IPaymentRepository paymentRepo) :
			base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo)
		{
			Contract.RequiresNonNull(daemon, nameof(daemon));
			Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
			Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

			this.daemon = daemon;
		}

		private readonly DaemonClient daemon;

		protected override string LogCategory => "Monero Payout Handler";

		#region IPayoutHandler

		public void Configure(PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			this.poolConfig = poolConfig;
			logger = LogUtil.GetPoolScopedLogger(typeof(MoneroPayoutHandler), poolConfig);

			daemon.Configure(poolConfig);
		}

		public string FormatRewardAmount(decimal amount)
		{
			return $"{amount:0.#####} {poolConfig.Coin.Type}";
		}

		public Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(blocks, nameof(blocks));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Block>();

			// TODO
			throw new NotImplementedException();
		}

		public Task PayoutAsync(Balance[] balances)
		{
			Contract.RequiresNonNull(balances, nameof(balances));

			logger.Info(() => $"[{LogCategory}] Paying out {FormatRewardAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

			// TODO
			throw new NotImplementedException();
		}

		#endregion // IPayoutHandler
	}
}
