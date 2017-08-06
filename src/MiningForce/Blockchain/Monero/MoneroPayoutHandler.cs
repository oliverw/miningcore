using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Schema;
using AutoMapper;
using CodeContracts;
using MiningForce.Blockchain.Monero.DaemonRequests;
using MiningForce.Blockchain.Monero.DaemonResponses;
using MiningForce.Configuration;
using MiningForce.DaemonInterface;
using MiningForce.Payments;
using MiningForce.Persistence;
using MiningForce.Persistence.Model;
using MiningForce.Persistence.Repositories;
using MiningForce.Util;
using MC = MiningForce.Blockchain.Monero.MoneroCommands;
using MWC = MiningForce.Blockchain.Monero.MoneroWalletCommands;

namespace MiningForce.Blockchain.Monero
{
	[CoinMetadata(CoinType.XMR)]
    public class MoneroPayoutHandler : PayoutHandlerBase,
		IPayoutHandler
	{
		public MoneroPayoutHandler(IConnectionFactory cf, IMapper mapper,
			DaemonClient daemon,
			DaemonClient walletDaemon,
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
			this.walletDaemon = walletDaemon;
		}

		private readonly DaemonClient daemon;
		private readonly DaemonClient walletDaemon;

		protected override string LogCategory => "Monero Payout Handler";

		#region IPayoutHandler

		public void Configure(PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			this.poolConfig = poolConfig;
			logger = LogUtil.GetPoolScopedLogger(typeof(MoneroPayoutHandler), poolConfig);

		    // configure standard daemon
		    var daemonEndpoints = poolConfig.Daemons
			    .Where(x => string.IsNullOrEmpty(x.Category))
			    .ToArray();

			daemon.Configure(daemonEndpoints, MoneroConstants.DaemonRpcLocation);

			// configure wallet daemon
			var walletDaemonEndpoints = poolConfig.Daemons
			    .Where(x => x.Category?.ToLower() == MoneroConstants.WalletDaemonCategory)
			    .ToArray();

			walletDaemon.Configure(walletDaemonEndpoints, MoneroConstants.DaemonRpcLocation);
		}

		public string FormatRewardAmount(decimal amount)
		{
			return $"{amount:0.#####} {poolConfig.Coin.Type}";
		}

		public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(blocks, nameof(blocks));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Block>();

			var immatureCount = 0;

			for (var i = 0; i < pageCount; i++)
			{
				// get a page full of blocks
				var page = blocks
					.Skip(i * pageSize)
					.Take(pageSize)
					.ToArray();

				// NOTE: monerod does not support batch-requests
				for (var j = 0; j < page.Length; j++)
				{
					var block = page[j];

					var rpcResult = await daemon.ExecuteCmdAnyAsync<GetBlockHeaderResponse>(
						MC.GetBlockHeaderByHeight,
						new GetBlockHeaderByHeightRequest
						{
							Height = block.Blockheight
						});

					if (rpcResult.Error != null)
					{
						logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {block.Blockheight}");
						continue;
					}

					if (rpcResult.Response?.BlockHeader == null)
					{
						logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {block.Blockheight}");
						continue;
					}

					var blockHeader = rpcResult.Response.BlockHeader;

					// orphaned?
					if (blockHeader.IsOrphaned || blockHeader.Hash != block.TransactionConfirmationData)
					{
						block.Status = BlockStatus.Orphaned;
						result.Add(block);
						continue;
					}

					// confirmed?
					if (blockHeader.Depth < MoneroConstants.PayoutMinConfirmations)
						continue;

					// matured and spendable 
					block.Status = BlockStatus.Confirmed;
					result.Add(block);
				}
			}

			//return result.ToArray();
			return new Block[0];
		}

		public Task PayoutAsync(Balance[] balances)
		{
			Contract.RequiresNonNull(balances, nameof(balances));

			logger.Info(() => $"[{LogCategory}] Paying out {FormatRewardAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

			// TODO
			return Task.FromResult(true);
		}

		#endregion // IPayoutHandler
	}
}
