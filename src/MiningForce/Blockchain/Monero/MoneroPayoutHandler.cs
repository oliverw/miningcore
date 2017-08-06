using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
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
using Block = MiningForce.Persistence.Model.Block;
using IBlockRepository = MiningForce.Persistence.Repositories.IBlockRepository;
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
		private MoneroNetworkType? networkType;

		protected override string LogCategory => "Monero Payout Handler";

		#region IPayoutHandler

		public void Configure(ClusterConfig clusterConfig, PoolConfig poolConfig)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

			this.poolConfig = poolConfig;
			this.clusterConfig = clusterConfig;

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

		public async Task<Block[]> ClassifyBlocksAsync(Block[] blocks)
		{
			Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
			Contract.RequiresNonNull(blocks, nameof(blocks));

			var pageSize = 100;
			var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
			var result = new List<Block>();

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
					if (blockHeader.Depth < MoneroConstants.PayoutMinBlockConfirmations)
						continue;

					// matured and spendable 
					block.Status = BlockStatus.Confirmed;
					block.Reward = (decimal)blockHeader.Reward / MoneroConstants.Piconero;
					result.Add(block);
				}
			}

			return result.ToArray();
		}

		public async Task UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, Block block, PoolConfig pool)
		{
			var blockRewardRemaining = block.Reward;

			// Distribute funds to configured reward recipients
			foreach (var recipient in poolConfig.RewardRecipients)
			{
				var amount = block.Reward * (recipient.Percentage / 100.0m);
				var address = recipient.Address;

				blockRewardRemaining -= amount;

				logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
				balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
			}

			// Tiny donation to MiningForce developer(s)
			if (!clusterConfig.DisableDevDonation &&
			    await GetNetworkTypeAsync() == MoneroNetworkType.Main)
			{
				var amount = block.Reward * MoneroConstants.DevReward;
				var address = MoneroConstants.DevAddress;

				blockRewardRemaining -= amount;

				logger.Info(() => $"Adding {FormatAmount(amount)} to balance of {address}");
				balanceRepo.AddAmount(con, tx, poolConfig.Id, poolConfig.Coin.Type, address, amount);
			}

			// update block-reward
			block.Reward = blockRewardRemaining;
		}

		public async Task PayoutAsync(Balance[] balances)
		{
			Contract.RequiresNonNull(balances, nameof(balances));

			// build request
			var request = new TransferRequest
			{
				Destinations = balances
					.Where(x => x.Amount > 0)
					.Select(x => new TransferDestination
					{
						Address = x.Address,
						Amount = (ulong) Math.Floor(x.Amount * MoneroConstants.Piconero)
					}).ToArray(),

				GetTxKey = true,
			};

			if (request.Destinations.Length == 0)
				return;

			logger.Info(() => $"[{LogCategory}] Paying out {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

			// send command
			var result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.Transfer, request);

			// gracefully handle error -4 (transaction would be too large. try /transfer_split)
			if (result.Error?.Code == -4)
			{
				logger.Info(() => $"[{LogCategory}] Retrying transfer using {MWC.TransferSplit}");

				result = await walletDaemon.ExecuteCmdAnyAsync<TransferResponse>(MWC.TransferSplit, request);
			}

			HandleTransferResponse(result, balances);
		}

		public string FormatAmount(decimal amount)
		{
			return $"{amount:0.#####} {poolConfig.Coin.Type}";
		}

		#endregion // IPayoutHandler

		void HandleTransferResponse(DaemonResponse<TransferResponse> response, Balance[] balances)
		{
			if (response.Error == null)
			{
				var txHash = response.Response.TxHash;

				// check result
				if (string.IsNullOrEmpty(txHash))
					logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' did not return a transaction id!");
				else
					logger.Info(() => $"[{LogCategory}] Payout transaction id: {txHash}, TxFee was {FormatAmount((decimal) response.Response.Fee / MoneroConstants.Piconero)}");

				PersistPayments(balances, txHash);
			}

			else
				logger.Error(() => $"[{LogCategory}] Daemon command '{MWC.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");
		}

		private async Task<MoneroNetworkType> GetNetworkTypeAsync()
		{
			if (!networkType.HasValue)
			{
				var infoResponse = await daemon.ExecuteCmdAnyAsync(MC.GetInfo);
				var info = infoResponse.Response.ToObject<GetInfoResponse>();

				networkType = info.IsTestnet ? MoneroNetworkType.Test : MoneroNetworkType.Main;
			}

			return networkType.Value;
		}
	}
}
