using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CodeContracts;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Configuration;
using MiningCore.DaemonInterface;
using MiningCore.Payments;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Util;

namespace MiningCore.Blockchain.Bitcoin
{
	[BitcoinCoinsMetaData]
    public class BitcoinPayoutHandler : PayoutHandlerBase,
		IPayoutHandler
	{
		public BitcoinPayoutHandler(IConnectionFactory cf, IMapper mapper,
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

		protected override string LogCategory => "Bitcoin Payout Handler";

		#region IPayoutHandler

		public void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			this.poolConfig = poolConfig;
			logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), poolConfig);

			daemon.Configure(poolConfig.Daemons);
		}

		public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(blocks, nameof(blocks));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Block>();

			var immatureCount = 0;

			for (int i = 0; i < pageCount; i++)
			{
				// get a page full of blocks
				var page = blocks
					.Skip(i * pageSize)
					.Take(pageSize)
					.ToArray();

				// build command batch (block.TransactionConfirmationData is the hash of the blocks coinbase transaction)
				var batch = page.Select(block => new DaemonCmd(BitcoinCommands.GetTransaction,
					new[] {block.TransactionConfirmationData})).ToArray();

				// execute batch
				var results = await daemon.ExecuteBatchAnyAsync(batch);

				for (int j = 0; j < results.Length; j++)
				{
					var cmdResult = results[j];

					var transactionInfo = cmdResult.Response?.ToObject<GetTransactionResponse>();
					var block = page[j];

					// check error
					if (cmdResult.Error != null)
					{
						// Code -5 interpreted as "orphaned"
						if (cmdResult.Error.Code == -5)
						{
							block.Status = BlockStatus.Orphaned;
							result.Add(block);
						}

						else
						{
							logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
							continue;
						}
					}

					// missing transaction details are interpreted as "orphaned"
					else if (transactionInfo?.Details == null || transactionInfo.Details.Length == 0)
					{
						block.Status = BlockStatus.Orphaned;
						result.Add(block);
					}

					else
					{
						switch (transactionInfo.Details[0].Category)
						{
							case "immature":
								// coinbase transaction that is not spendable yet, do nothing and let it mature
								immatureCount++;
								break;

							case "generate":
								// matured and spendable coinbase transaction
								block.Status = BlockStatus.Confirmed;
								result.Add(block);
								break;

							default:
								block.Status = BlockStatus.Orphaned;
								result.Add(block);
								break;
						}
					}
				}
			}

			return result.ToArray();
		}

		public Task UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
		{
			// reward-payouts are handled through coinbase-tx for bitcoin and family
			return Task.FromResult(false);
		}

		public async Task PayoutAsync(Balance[] balances)
		{
			Contract.RequiresNonNull(balances, nameof(balances));

			// build args
			var amounts = balances
				.Where(x=> x.Amount > 0)
				.ToDictionary(x => x.Address, x => x.Amount);

			if (amounts.Count == 0)
				return;

			logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

			var subtractFeesFrom = amounts.Keys.ToArray();

			var args = new object[]
			{
				string.Empty,			// default account 
				amounts,				// addresses and asscociated amounts
				1,						// only spend funds covered by this many confirmations
				"MiningCore Payout",	// comment
				subtractFeesFrom		// distribute transaction fee equally over all recipients
			};

			// send command
			var result = await daemon.ExecuteCmdAnyAsync<string>(BitcoinCommands.SendMany, args);

			if (result.Error == null)
			{
				var txId = result.Response;

				// check result
				if (string.IsNullOrEmpty(txId))
					logger.Error(() => $"[{LogCategory}] Daemon command '{BitcoinCommands.SendMany}' did not return a transaction id!");
				else
					logger.Info(() => $"[{LogCategory}] Payout transaction id: {txId}");

				PersistPayments(balances, txId);
			}

			else
				logger.Error(() => $"[{LogCategory}] Daemon command '{BitcoinCommands.SendMany}' returned error: {result.Error.Message} code {result.Error.Code}");
		}

		public string FormatAmount(decimal amount)
		{
			return $"{amount:0.#####} {poolConfig.Coin.Type}";
		}

		#endregion // IPayoutHandler
	}
}
