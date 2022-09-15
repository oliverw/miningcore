using System.Data;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Cryptonote.Configuration;
using Miningcore.Blockchain.Cryptonote.DaemonRequests;
using Miningcore.Blockchain.Cryptonote.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Contract = Miningcore.Contracts.Contract;
using CNC = Miningcore.Blockchain.Cryptonote.CryptonoteCommands;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Cryptonote;

[CoinFamily(CoinFamily.Cryptonote)]
public class CryptonotePayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public CryptonotePayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        IMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IMessageBus messageBus) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);

        this.ctx = ctx;
    }

    private readonly IComponentContext ctx;
    private RpcClient rpcClient;
    private RpcClient rpcClientWallet;
    private CryptonoteNetworkType? networkType;
    private CryptonotePoolPaymentProcessingConfigExtra extraConfig;
    private bool walletSupportsTransferSplit;

    protected override string LogCategory => "Cryptonote Payout Handler";

    private async Task<bool> HandleTransferResponseAsync(RpcResponse<TransferResponse> response, params Balance[] balances)
    {
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        if(response.Error == null)
        {
            var txHash = response.Response.TxHash;
            var txFee = response.Response.Fee / coin.SmallestUnit;

            logger.Info(() => $"[{LogCategory}] Payment transaction id: {txHash}, TxFee {FormatAmount(txFee)}, TxKey {response.Response.TxKey}");

            await PersistPaymentsAsync(balances, txHash);
            NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txHash }, txFee);
            return true;
        }

        else
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{CryptonoteWalletCommands.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}");

            NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{CryptonoteWalletCommands.Transfer}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            return false;
        }
    }

    private async Task<bool> HandleTransferResponseAsync(RpcResponse<TransferSplitResponse> response, params Balance[] balances)
    {
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        if(response.Error == null)
        {
            var txHashes = response.Response.TxHashList;
            var txFees = response.Response.FeeList.Select(x => x / coin.SmallestUnit).ToArray();

            logger.Info(() => $"[{LogCategory}] Split-Payment transaction ids: {string.Join(", ", txHashes)}, Corresponding TxFees were {string.Join(", ", txFees.Select(FormatAmount))}");

            await PersistPaymentsAsync(balances, txHashes.First());
            NotifyPayoutSuccess(poolConfig.Id, balances, txHashes, txFees.Sum());
            return true;
        }

        else
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{CryptonoteWalletCommands.TransferSplit}' returned error: {response.Error.Message} code {response.Error.Code}");

            NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{CryptonoteWalletCommands.TransferSplit}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            return false;
        }
    }

    private async Task UpdateNetworkTypeAsync(CancellationToken ct)
    {
        if(!networkType.HasValue)
        {
            var infoResponse = await rpcClient.ExecuteAsync(logger, CNC.GetInfo, ct, true);
            var info = infoResponse.Response.ToObject<GetInfoResponse>();

            if(info == null)
                throw new PoolStartupException($"{LogCategory}] Unable to determine network type", poolConfig.Id);

            // chain detection
            if(!string.IsNullOrEmpty(info.NetType))
            {
                switch(info.NetType.ToLower())
                {
                    case "mainnet":
                        networkType = CryptonoteNetworkType.Main;
                        break;
                    case "stagenet":
                        networkType = CryptonoteNetworkType.Stage;
                        break;
                    case "testnet":
                        networkType = CryptonoteNetworkType.Test;
                        break;
                    default:
                        throw new PoolStartupException($"Unsupported net type '{info.NetType}'", poolConfig.Id);
                }
            }

            else
                networkType = info.IsTestnet ? CryptonoteNetworkType.Test : CryptonoteNetworkType.Main;
        }
    }

    private async Task<bool> EnsureBalance(decimal requiredAmount, CryptonoteCoinTemplate coin, CancellationToken ct)
    {
        var response = await rpcClientWallet.ExecuteAsync<GetBalanceResponse>(logger, CryptonoteWalletCommands.GetBalance, ct);

        if(response.Error != null)
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{CryptonoteWalletCommands.GetBalance}' returned error: {response.Error.Message} code {response.Error.Code}");
            return false;
        }

        var unlockedBalance = Math.Floor(response.Response.UnlockedBalance / coin.SmallestUnit);
        var balance = Math.Floor(response.Response.Balance / coin.SmallestUnit);

        if(unlockedBalance < requiredAmount)
        {
            logger.Info(() => $"[{LogCategory}] {FormatAmount(requiredAmount)} unlocked balance required for payment, but only have {FormatAmount(unlockedBalance)} of {FormatAmount(balance)} available yet. Will try again.");
            return false;
        }

        logger.Info(() => $"[{LogCategory}] Current balance is {FormatAmount(unlockedBalance)}");
        return true;
    }

    private async Task<bool> PayoutBatch(Balance[] balances, CancellationToken ct)
    {
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        // ensure there's enough balance
        if(!await EnsureBalance(balances.Sum(x => x.Amount), coin, ct))
            return false;

        // build request
        var request = new TransferRequest
        {
            Destinations = balances
                .Where(x => x.Amount > 0)
                .Select(x =>
                {
                    ExtractAddressAndPaymentId(x.Address, out var address, out _);

                    return new TransferDestination
                    {
                        Address = address,
                        Amount = (ulong) Math.Floor(x.Amount * coin.SmallestUnit)
                    };
                }).ToArray(),

            GetTxKey = true
        };

        if(request.Destinations.Length == 0)
            return true;

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses:\n{string.Join("\n", balances.OrderByDescending(x => x.Amount).Select(x => $"{FormatAmount(x.Amount)} to {x.Address}"))}");

        // send command
        var transferResponse = await rpcClientWallet.ExecuteAsync<TransferResponse>(logger, CryptonoteWalletCommands.Transfer, ct, request);

        // gracefully handle error -4 (transaction would be too large. try /transfer_split)
        if(transferResponse.Error?.Code == -4)
        {
            if(walletSupportsTransferSplit)
            {
                logger.Error(() => $"[{LogCategory}] Daemon command '{CryptonoteWalletCommands.Transfer}' returned error: {transferResponse.Error.Message} code {transferResponse.Error.Code}");
                logger.Info(() => $"[{LogCategory}] Retrying transfer using {CryptonoteWalletCommands.TransferSplit}");

                var transferSplitResponse = await rpcClientWallet.ExecuteAsync<TransferSplitResponse>(logger, CryptonoteWalletCommands.TransferSplit, ct, request);

                return await HandleTransferResponseAsync(transferSplitResponse, balances);
            }
        }

        return await HandleTransferResponseAsync(transferResponse, balances);
    }

    private void ExtractAddressAndPaymentId(string input, out string address, out string paymentId)
    {
        paymentId = null;
        var index = input.IndexOf(PayoutConstants.PayoutInfoSeperator);

        if(index != -1)
        {
            address = input[..index];

            if(index + 1 < input.Length)
            {
                paymentId = input[(index + 1)..];

                // ignore invalid payment ids
                if(paymentId.Length != CryptonoteConstants.PaymentIdHexLength)
                    paymentId = null;
            }
        }

        else
            address = input;
    }

    private async Task<bool> PayoutToPaymentId(Balance balance, CancellationToken ct)
    {
        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

        ExtractAddressAndPaymentId(balance.Address, out var address, out var paymentId);
        var isIntegratedAddress = string.IsNullOrEmpty(paymentId);

        // ensure there's enough balance
        if(!await EnsureBalance(balance.Amount, coin, ct))
            return false;

        // build request
        var request = new TransferRequest
        {
            Destinations = new[]
            {
                new TransferDestination
                {
                    Address = address,
                    Amount = (ulong) Math.Floor(balance.Amount * coin.SmallestUnit)
                }
            },
            PaymentId = paymentId,
            GetTxKey = true
        };

        if(!isIntegratedAddress)
            request.PaymentId = paymentId;

        if(!isIntegratedAddress)
            logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balance.Amount)} to address {balance.Address} with paymentId {paymentId}");
        else
            logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balance.Amount)} to integrated address {balance.Address}");

        // send command
        var result = await rpcClientWallet.ExecuteAsync<TransferResponse>(logger, CryptonoteWalletCommands.Transfer, ct, request);

        if(walletSupportsTransferSplit)
        {
            // gracefully handle error -4 (transaction would be too large. try /transfer_split)
            if(result.Error?.Code == -4)
            {
                logger.Info(() => $"[{LogCategory}] Retrying transfer using {CryptonoteWalletCommands.TransferSplit}");

                result = await rpcClientWallet.ExecuteAsync<TransferResponse>(logger, CryptonoteWalletCommands.TransferSplit, ct, request);
            }
        }

        return await HandleTransferResponseAsync(result, balance);
    }

    #region IPayoutHandler

    public async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;
        extraConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<CryptonotePoolPaymentProcessingConfigExtra>();

        logger = LogUtil.GetPoolScopedLogger(typeof(CryptonotePayoutHandler), pc);

        // configure standard daemon
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        var daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClient = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // configure wallet daemon
        var walletDaemonEndpoints = pc.Daemons
            .Where(x => x.Category?.ToLower() == CryptonoteConstants.WalletDaemonCategory)
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = CryptonoteConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClientWallet = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // detect network
        await UpdateNetworkTypeAsync(ct);

        // detect transfer_split support
        var response = await rpcClientWallet.ExecuteAsync<TransferResponse>(logger, CryptonoteWalletCommands.TransferSplit, ct);
        walletSupportsTransferSplit = response.Error.Code != CryptonoteConstants.MoneroRpcMethodNotFound;
    }

    public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // NOTE: monerod does not support batch-requests
            for(var j = 0; j < page.Length; j++)
            {
                var block = page[j];

                var rpcResult = await rpcClient.ExecuteAsync<GetBlockHeaderResponse>(logger,
                    CNC.GetBlockHeaderByHeight, ct,
                    new GetBlockHeaderByHeightRequest
                    {
                        Height = block.BlockHeight
                    });

                if(rpcResult.Error != null)
                {
                    logger.Debug(() => $"[{LogCategory}] Daemon reports error '{rpcResult.Error.Message}' (Code {rpcResult.Error.Code}) for block {block.BlockHeight}");
                    continue;
                }

                if(rpcResult.Response?.BlockHeader == null)
                {
                    logger.Debug(() => $"[{LogCategory}] Daemon returned no header for block {block.BlockHeight}");
                    continue;
                }

                var blockHeader = rpcResult.Response.BlockHeader;

                // update progress
                block.ConfirmationProgress = Math.Min(1.0d, (double) blockHeader.Depth / CryptonoteConstants.PayoutMinBlockConfirmations);
                result.Add(block);

                messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);

                // orphaned?
                if(blockHeader.IsOrphaned || blockHeader.Hash != block.TransactionConfirmationData)
                {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    continue;
                }

                // matured and spendable?
                if(blockHeader.Depth >= CryptonoteConstants.PayoutMinBlockConfirmations)
                {
                    block.Status = BlockStatus.Confirmed;
                    block.ConfirmationProgress = 1;
                    block.Reward = (blockHeader.Reward / coin.SmallestUnit) * coin.BlockrewardMultiplier;

                    logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }
            }
        }

        return result.ToArray();
    }

    public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx,
        IMiningPool pool, Block block, CancellationToken ct)
    {
        var blockRewardRemaining = await base.UpdateBlockRewardBalancesAsync(con, tx, pool, block, ct);

        // Deduct static reserve for tx fees
        blockRewardRemaining -= CryptonoteConstants.StaticTransactionFeeReserve;

        return blockRewardRemaining;
    }

    public async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        var coin = poolConfig.Template.As<CryptonoteCoinTemplate>();

#if !DEBUG // ensure we have peers
            var infoResponse = await rpcClient.ExecuteAsync<GetInfoResponse>(logger, CNC.GetInfo, ct);
            if (infoResponse.Error != null || infoResponse.Response == null ||
                infoResponse.Response.IncomingConnectionsCount + infoResponse.Response.OutgoingConnectionsCount < 3)
            {
                logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
                return;
            }
#endif
        // validate addresses
        balances = balances
            .Where(x =>
            {
                ExtractAddressAndPaymentId(x.Address, out var address, out _);

                var addressPrefix = CryptonoteBindings.DecodeAddress(address);
                var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);

                switch(networkType)
                {
                    case CryptonoteNetworkType.Main:
                        if(addressPrefix != coin.AddressPrefix &&
                           addressIntegratedPrefix != coin.AddressPrefixIntegrated)
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;

                    case CryptonoteNetworkType.Stage:
                        if(addressPrefix != coin.AddressPrefixStagenet &&
                           addressIntegratedPrefix != coin.AddressPrefixIntegratedStagenet)
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;

                    case CryptonoteNetworkType.Test:
                        if(addressPrefix != coin.AddressPrefixTestnet &&
                           addressIntegratedPrefix != coin.AddressPrefixIntegratedTestnet)
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;
                }

                return true;
            })
            .ToArray();

        // simple balances first
        var simpleBalances = balances
            .Where(x =>
            {
                ExtractAddressAndPaymentId(x.Address, out var address, out var paymentId);

                var hasPaymentId = paymentId != null;
                var isIntegratedAddress = false;
                var addressIntegratedPrefix = CryptonoteBindings.DecodeIntegratedAddress(address);

                switch(networkType)
                {
                    case CryptonoteNetworkType.Main:
                        if(addressIntegratedPrefix == coin.AddressPrefixIntegrated)
                            isIntegratedAddress = true;
                        break;

                    case CryptonoteNetworkType.Test:
                        if(addressIntegratedPrefix == coin.AddressPrefixIntegratedTestnet)
                            isIntegratedAddress = true;
                        break;
                }

                return !hasPaymentId && !isIntegratedAddress;
            })
            .OrderByDescending(x => x.Amount)
            .ToArray();

        if(simpleBalances.Length > 0)
#if false
                await PayoutBatch(simpleBalances);
#else
        {
            var maxBatchSize = 15;  // going over 15 yields "sv/gamma are too large"
            var pageSize = maxBatchSize;
            var pageCount = (int) Math.Ceiling((double) simpleBalances.Length / pageSize);

            for(var i = 0; i < pageCount; i++)
            {
                var page = simpleBalances
                    .Skip(i * pageSize)
                    .Take(pageSize)
                    .ToArray();

                if(!await PayoutBatch(page, ct))
                    break;
            }
        }
#endif
        // balances with paymentIds
        var minimumPaymentToPaymentId = extraConfig?.MinimumPaymentToPaymentId ?? poolConfig.PaymentProcessing.MinimumPayment;

        var paymentIdBalances = balances.Except(simpleBalances)
            .Where(x => x.Amount >= minimumPaymentToPaymentId)
            .ToArray();

        foreach(var balance in paymentIdBalances)
        {
            if(!await PayoutToPaymentId(balance, ct))
                break;
        }

        // save wallet
        await rpcClientWallet.ExecuteAsync<JToken>(logger, CryptonoteWalletCommands.Store, ct);
    }

    public double AdjustBlockEffort(double effort)
    {
        return effort;
    }

    #endregion // IPayoutHandler
}
