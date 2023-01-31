using System.Data;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Conceal.Configuration;
using Miningcore.Blockchain.Conceal.DaemonRequests;
using Miningcore.Blockchain.Conceal.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Native;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rest;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Contract = Miningcore.Contracts.Contract;
using CNC = Miningcore.Blockchain.Conceal.ConcealCommands;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Conceal;

[CoinFamily(CoinFamily.Conceal)]
public class ConcealPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public ConcealPayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        IMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IHttpClientFactory httpClientFactory,
        IMessageBus messageBus) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx);
        Contract.RequiresNonNull(balanceRepo);
        Contract.RequiresNonNull(paymentRepo);

        this.ctx = ctx;
        this.httpClientFactory = httpClientFactory;
    }

    private readonly IComponentContext ctx;
    private IHttpClientFactory httpClientFactory;
    private SimpleRestClient restClient;
    private RpcClient rpcClient;
    private RpcClient rpcClientWallet;
    private ConcealNetworkType? networkType;
    private ConcealPoolPaymentProcessingConfigExtra extraConfig;
    private ConcealPoolConfigExtra extraPoolConfig;
    private bool walletSupportsSendTransaction;

    protected override string LogCategory => "Conceal Payout Handler";

    private async Task<bool> HandleSendTransactionResponseAsync(RpcResponse<SendTransactionResponse> response, params Balance[] balances)
    {
        var coin = poolConfig.Template.As<ConcealCoinTemplate>();

        if(response.Error == null)
        {
            var txHash = response.Response.TxHash;
            var txFee = ConcealConstants.StaticTransactionFeeReserve;

            logger.Info(() => $"[{LogCategory}] Payment transaction id: {txHash}, TxFee {FormatAmount(txFee)}");

            await PersistPaymentsAsync(balances, txHash);
            NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txHash }, txFee);
            return true;
        }

        else
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{ConcealWalletCommands.SendTransaction}' returned error: {response.Error.Message} code {response.Error.Code}");

            NotifyPayoutFailure(poolConfig.Id, balances, $"Daemon command '{ConcealWalletCommands.SendTransaction}' returned error: {response.Error.Message} code {response.Error.Code}", null);
            return false;
        }
    }

    private async Task<bool> EnsureBalance(decimal requiredAmount, ConcealCoinTemplate coin, CancellationToken ct)
    {
        //var response = await rpcClientWallet.ExecuteAsync<GetBalanceResponse>(logger, ConcealWalletCommands.GetBalance, ct);
        var request = new GetBalanceRequest
        {
            Address = poolConfig.Address
        };

        var response = await rpcClientWallet.ExecuteAsync<GetBalanceResponse>(logger, ConcealWalletCommands.GetBalance, ct, request);
        
        if(response.Error != null)
        {
            logger.Error(() => $"[{LogCategory}] Daemon command '{ConcealWalletCommands.GetBalance}' returned error: {response.Error.Message} code {response.Error.Code}");
            return false;
        }

        var balance = Math.Floor(response.Response.Balance / coin.SmallestUnit);

        if(balance < requiredAmount)
        {
            logger.Info(() => $"[{LogCategory}] {FormatAmount(requiredAmount)} required for payment, but only have {FormatAmount(balance)} available yet. Will try again.");
            return false;
        }

        logger.Info(() => $"[{LogCategory}] Current balance is {FormatAmount(balance)}");
        return true;
    }

    private async Task<bool> PayoutBatch(Balance[] balances, CancellationToken ct)
    {
        var coin = poolConfig.Template.As<ConcealCoinTemplate>();

        // ensure there's enough balance
        if(!await EnsureBalance(balances.Sum(x => x.Amount), coin, ct))
            return false;

        // build request
        var request = new SendTransactionRequest
        {
            Addresses = new string[]
            {
                poolConfig.Address
            },
            Transfers = balances
                .Where(x => x.Amount > 0)
                .Select(x =>
                {
                    ExtractAddressAndPaymentId(x.Address, out var address, out _);
                    
                    logger.Debug(() => $"[{LogCategory}] [batch] ['address': '{x.Address} - {address}', 'amount': {Math.Floor(x.Amount * coin.SmallestUnit)}]");
                    
                    return new SendTransactionTransfers
                    {
                        Address = address,
                        Amount = (ulong) Math.Floor(x.Amount * coin.SmallestUnit)
                    };
                }).ToArray(),
            ChangeAddress = poolConfig.Address
        };

        if(request.Transfers.Length == 0)
            return true;
        
        logger.Debug(() => $"[{LogCategory}] [batch] RPC data: ['anonymity': {request.Anonymity}, 'fee': {request.Fee}, 'unlockTime': {request.UnlockTime}, 'changeAddress': '{request.ChangeAddress}']");
        logger.Info(() => $"[{LogCategory}] [batch] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses:\n{string.Join("\n", balances.OrderByDescending(x => x.Amount).Select(x => $"{FormatAmount(x.Amount)} to {x.Address}"))}");

        // send command
        var sendTransactionResponse = await rpcClientWallet.ExecuteAsync<SendTransactionResponse>(logger, ConcealWalletCommands.SendTransaction, ct, request);

        return await HandleSendTransactionResponseAsync(sendTransactionResponse, balances);
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
                if(paymentId.Length != ConcealConstants.PaymentIdHexLength)
                    paymentId = null;
            }
        }

        else
            address = input;
    }

    private async Task<bool> PayoutToPaymentId(Balance balance, CancellationToken ct)
    {
        var coin = poolConfig.Template.As<ConcealCoinTemplate>();

        ExtractAddressAndPaymentId(balance.Address, out var address, out var paymentId);
        var isIntegratedAddress = string.IsNullOrEmpty(paymentId);

        // ensure there's enough balance
        if(!await EnsureBalance(balance.Amount, coin, ct))
            return false;

        // build request
        var request = new SendTransactionRequest
        {
            Addresses = new string[]
            {
                poolConfig.Address
            },
            Transfers = new[]
            {
                new SendTransactionTransfers
                {
                    Address = address,
                    Amount = (ulong) Math.Floor(balance.Amount * coin.SmallestUnit)
                }
            },
            ChangeAddress = poolConfig.Address
        };
        
        if(!isIntegratedAddress)
            logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balance.Amount)} to address {balance.Address} with paymentId {paymentId}");
        else
            logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balance.Amount)} to integrated address {balance.Address}");

        // send command
        var result = await rpcClientWallet.ExecuteAsync<SendTransactionResponse>(logger, ConcealWalletCommands.SendTransaction, ct, request);

        return await HandleSendTransactionResponseAsync(result, balance);
    }

    #region IPayoutHandler

    public async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;
        extraConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<ConcealPoolPaymentProcessingConfigExtra>();
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<ConcealPoolConfigExtra>();
        
        var NetworkTypeOverride = !string.IsNullOrEmpty(extraPoolConfig.NetworkTypeOverride) ? extraPoolConfig.NetworkTypeOverride : "testnet";
        
        switch(NetworkTypeOverride.ToLower())
        {
            case "mainnet":
                networkType = ConcealNetworkType.Main;
                break;
            case "testnet":
                networkType = ConcealNetworkType.Test;
                break;
            default:
                throw new PoolStartupException($"Unsupport net type '{NetworkTypeOverride}'", poolConfig.Id);
        }

        logger = LogUtil.GetPoolScopedLogger(typeof(ConcealPayoutHandler), pc);

        // configure standard daemon
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
        
        var daemonEndpoints = pc.Daemons
            .Where(x => string.IsNullOrEmpty(x.Category))
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = ConcealConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();
            
        restClient = new SimpleRestClient(httpClientFactory, "http://" + daemonEndpoints.First().Host.ToString() + ":" + daemonEndpoints.First().Port.ToString() + "/");
        rpcClient = new RpcClient(daemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);
        
        // configure wallet daemon
        var walletDaemonEndpoints = pc.Daemons
            .Where(x => x.Category?.ToLower() == ConcealConstants.WalletDaemonCategory)
            .Select(x =>
            {
                if(string.IsNullOrEmpty(x.HttpPath))
                    x.HttpPath = ConcealConstants.DaemonRpcLocation;

                return x;
            })
            .ToArray();

        rpcClientWallet = new RpcClient(walletDaemonEndpoints.First(), jsonSerializerSettings, messageBus, pc.Id);

        // detect sendTransaction support
        var response = await rpcClientWallet.ExecuteAsync<SendTransactionResponse>(logger, ConcealWalletCommands.SendTransaction, ct);
        walletSupportsSendTransaction = response.Error.Code != ConcealConstants.ConcealRpcMethodNotFound;
    }

    public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var coin = poolConfig.Template.As<ConcealCoinTemplate>();
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

            // NOTE: conceald does not support batch-requests???
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
                block.ConfirmationProgress = Math.Min(1.0d, (double) blockHeader.Depth / ConcealConstants.PayoutMinBlockConfirmations);
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
                if(blockHeader.Depth >= ConcealConstants.PayoutMinBlockConfirmations)
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
        blockRewardRemaining -= ConcealConstants.StaticTransactionFeeReserve;

        return blockRewardRemaining;
    }

    public async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        var coin = poolConfig.Template.As<ConcealCoinTemplate>();

#if !DEBUG // ensure we have peers
            var infoResponse = await restClient.Get<GetInfoResponse>(CNC.GetInfo, ct);
            
            if (infoResponse.Status != "OK" ||
                infoResponse.IncomingConnectionsCount + infoResponse.OutgoingConnectionsCount < 3)
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
                    case ConcealNetworkType.Main:
                        if(addressPrefix != coin.AddressPrefix)
                        {
                            logger.Warn(() => $"[{LogCategory}] Excluding payment to invalid address: {x.Address}");
                            return false;
                        }

                        break;
                    
                    case ConcealNetworkType.Test:
                        if(addressPrefix != coin.AddressPrefixTestnet)
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
                    case ConcealNetworkType.Main:
                        if(addressIntegratedPrefix == coin.AddressPrefixIntegrated)
                            isIntegratedAddress = true;
                        break;

                    case ConcealNetworkType.Test:
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
        await rpcClientWallet.ExecuteAsync<JToken>(logger, ConcealWalletCommands.Save, ct);
    }

    public double AdjustBlockEffort(double effort)
    {
        return effort;
    }

    #endregion // IPayoutHandler
}