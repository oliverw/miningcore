using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using CodeContracts;
using MiningForce.Blockchain.Daemon;
using MiningForce.Configuration;
using MiningForce.Payments;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;
using MiningForce.Util;
using Newtonsoft.Json.Linq;
using BDC = MiningForce.Blockchain.Bitcoin.BitcoinDaemonCommands;

namespace MiningForce.Blockchain.Bitcoin
{
    public class BitcoinPayoutHandler : PayoutHandlerBase,
		IPayoutHandler
	{
		public BitcoinPayoutHandler(IConnectionFactory cf, IMapper mapper,
			IShareRepository shareRepo, IBlockRepository blockRepo, DaemonClient daemon) :
			base(cf, mapper, shareRepo, blockRepo)
		{
			Contract.RequiresNonNull(daemon, nameof(daemon));

			this.daemon = daemon;
		}

		private PoolConfig poolConfig;
		private readonly DaemonClient daemon;

		#region IPayoutHandler

		public void Configure(PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			this.poolConfig = poolConfig;
			logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), poolConfig);

			daemon.Configure(poolConfig);
		}

		public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Block>();

			for (int i = 0; i < pageCount; i++)
			{
				// get a page full of blocks
				var page = blocks
					.Skip(i * pageSize)
					.Take(pageCount)
					.ToArray();

				// build command batch (block.TransactionConfirmationData is the hash of the blocks coinbase transaction)
				var batch = page.Select(block => new DaemonCmd(BDC.GetTransaction,
					new[] {block.TransactionConfirmationData})).ToArray();

				// execute batch
				var results = await daemon.ExecuteBatchAnyAsync(batch);

				for (int j = 0; j < results.Length; j++)
				{
					var cmdResult = results[j];

					var transactionInfo = cmdResult.Response?.ToObject<DaemonResults.GetTransactionResult>();
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
							logger.Warn(() => $"Daemon report error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
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
								break;

							case "generate":
								// matured and spendable coinbase transaction
								block.Status = BlockStatus.Confirmed;
								block.Reward = transactionInfo.Details[0].Amount / BitcoinConstants.SatoshisPerBitcoin;
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

		public async Task<double> GetNetworkDifficultyAsync()
		{
			var result = await daemon.ExecuteCmdAnyAsync<JToken>(BDC.GetDifficulty);
			return result.Response.ToObject<double>();
		}

		public string FormatRewardAmount(double amount)
		{
			// assumes amount in satoshis (as returned from GetTransaction)
			return $"{amount:0.#####} {poolConfig.Coin.Type}";
		}

		public Task PayoutAsync(Balance balance)
		{
			return Task.FromResult(true);
		}

		#endregion // IPayoutHandler
	}
}
