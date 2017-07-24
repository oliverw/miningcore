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

		public async Task<Persistence.Model.Block[]> ClassifyBlocksAsync(Persistence.Model.Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Persistence.Model.Block>();

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
					var transactionInfo = results[j].Response.ToObject<DaemonResults.GetTransactionResult>();
					var block = page[j];

					// missing transaction details are interpreted as "orphaned"
					if (transactionInfo.Details == null || transactionInfo.Details.Length == 0)
					{
						block.Status = Persistence.Model.BlockStatus.Orphaned;
						result.Add(block);
					}

					else
					{
						switch (transactionInfo.Details[0].Category)
						{
							case "immature":
								// coinbase transaction that is not spendable yet
								// do nothing and let it mature
								break;

							case "generate":
								// matured and spendable coinbase transaction
								block.Status = Persistence.Model.BlockStatus.Confirmed;
								block.Reward = transactionInfo.Details[0].Amount;
								result.Add(block);
								break;

							default:
								block.Status = Persistence.Model.BlockStatus.Orphaned;
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

		#endregion // IPayoutHandler
	}
}
