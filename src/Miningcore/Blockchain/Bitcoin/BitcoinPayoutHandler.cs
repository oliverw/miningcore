using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Bitcoin.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Bitcoin;

[CoinFamily(CoinFamily.Bitcoin)]
public class BitcoinPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public BitcoinPayoutHandler(
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

    protected readonly IComponentContext ctx;
    protected RpcClient rpcClient;
    protected BitcoinDaemonEndpointConfigExtra extraPoolConfig;
    protected BitcoinPoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

    protected override string LogCategory => "Bitcoin Payout Handler";

    #region IPayoutHandler

    public virtual Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<BitcoinDaemonEndpointConfigExtra>();
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<BitcoinPoolPaymentProcessingConfigExtra>();

        logger = LogUtil.GetPoolScopedLogger(typeof(BitcoinPayoutHandler), pc);

        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();
        rpcClient = new RpcClient(pc.Daemons.First(), jsonSerializerSettings, messageBus, pc.Id);

        return Task.FromResult(true);
    }

    public virtual async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var coin = poolConfig.Template.As<CoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();
        int minConfirmations;

        if(coin is BitcoinTemplate bitcoinTemplate)
            minConfirmations = extraPoolConfig?.MinimumConfirmations ?? bitcoinTemplate.CoinbaseMinConfimations ?? BitcoinConstants.CoinbaseMinConfimations;
        else
            minConfirmations = extraPoolConfig?.MinimumConfirmations ?? BitcoinConstants.CoinbaseMinConfimations;

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // build command batch (block.TransactionConfirmationData is the hash of the blocks coinbase transaction)
            var batch = page.Select(block => new RpcRequest(BitcoinCommands.GetTransaction,
                new[] { block.TransactionConfirmationData })).ToArray();

            // execute batch
            var results = await rpcClient.ExecuteBatchAsync(logger, ct, batch);

            for(var j = 0; j < results.Length; j++)
            {
                var cmdResult = results[j];

                var transactionInfo = cmdResult.Response?.ToObject<Transaction>();
                var block = page[j];

                // check error
                if(cmdResult.Error != null)
                {
                    // Code -5 interpreted as "orphaned"
                    if(cmdResult.Error.Code == -5)
                    {
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                        result.Add(block);

                        logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to daemon error {cmdResult.Error.Code}");

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }

                    else
                        logger.Warn(() => $"[{LogCategory}] Daemon reports error '{cmdResult.Error.Message}' (Code {cmdResult.Error.Code}) for transaction {page[j].TransactionConfirmationData}");
                }

                // missing transaction details are interpreted as "orphaned"
                else if(transactionInfo?.Details == null || transactionInfo.Details.Length == 0)
                {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;
                    result.Add(block);

                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due to missing tx details");
                }

                else
                {
                    switch(transactionInfo.Details[0].Category)
                    {
                        case "immature":
                            // update progress
                            block.ConfirmationProgress = Math.Min(1.0d, (double) transactionInfo.Confirmations / minConfirmations);
                            block.Reward = transactionInfo.Amount;  // update actual block-reward from coinbase-tx
                            result.Add(block);

                            messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
                            break;

                        case "generate":
                            // matured and spendable coinbase transaction
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.Reward = transactionInfo.Amount;  // update actual block-reward from coinbase-tx
                            result.Add(block);

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                            break;

                        default:
                            logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned. Category: {transactionInfo.Details[0].Category}");

                            block.Status = BlockStatus.Orphaned;
                            block.Reward = 0;
                            result.Add(block);

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                            break;
                    }
                }
            }
        }

        return result.ToArray();
    }

    public virtual Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct)
    {
        block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

        return Task.FromResult(true);
    }

    public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        // build args
        var amounts = balances
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Address, x => Math.Round(x.Amount, 4));

        if(amounts.Count == 0)
            return;

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");

        object[] args;

        if(extraPoolPaymentProcessingConfig?.MinersPayTxFees == true)
        {
            var identifier = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.CoinbaseString) ?
                clusterConfig.PaymentProcessing.CoinbaseString.Trim() : "Miningcore";

            var comment = $"{identifier} Payment";
            var subtractFeesFrom = amounts.Keys.ToArray();

            if(!poolConfig.Template.As<BitcoinTemplate>().HasMasterNodes)
            {
                args = new object[]
                {
                    string.Empty,       // default account
                    amounts,            // addresses and associated amounts
                    1,                  // only spend funds covered by this many confirmations
                    comment,            // tx comment
                    subtractFeesFrom,   // distribute transaction fee equally over all recipients
                };
            }

            else
            {
                args = new object[]
                {
                    string.Empty,       // default account
                    amounts,            // addresses and associated amounts
                    1,                  // only spend funds covered by this many confirmations
                    false,              // Whether to add confirmations to transactions locked via InstantSend
                    comment,            // tx comment
                    subtractFeesFrom,   // distribute transaction fee equally over all recipients
                    false,              // use_is: Send this transaction as InstantSend
                    false,              // Use anonymized funds only
                };
            }
        }

        else
        {
            args = new object[]
            {
                string.Empty, // default account
                amounts, // addresses and associated amounts
            };
        }

        var didUnlockWallet = false;

        // send command
        tryTransfer:
        var result = await rpcClient.ExecuteAsync<string>(logger, BitcoinCommands.SendMany, ct, args);

        if(result.Error == null)
        {
            if(didUnlockWallet)
            {
                // lock wallet
                logger.Info(() => $"[{LogCategory}] Locking wallet");
                await rpcClient.ExecuteAsync<JToken>(logger, BitcoinCommands.WalletLock, ct);
            }

            // check result
            var txId = result.Response;

            if(string.IsNullOrEmpty(txId))
                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} did not return a transaction id!");
            else
                logger.Info(() => $"[{LogCategory}] Payment transaction id: {txId}");

            await PersistPaymentsAsync(balances, txId);

            NotifyPayoutSuccess(poolConfig.Id, balances, new[] { txId }, null);
        }

        else
        {
            if(result.Error.Code == (int) BitcoinRPCErrorCode.RPC_WALLET_UNLOCK_NEEDED && !didUnlockWallet)
            {
                if(!string.IsNullOrEmpty(extraPoolPaymentProcessingConfig?.WalletPassword))
                {
                    logger.Info(() => $"[{LogCategory}] Unlocking wallet");

                    var unlockResult = await rpcClient.ExecuteAsync<JToken>(logger, BitcoinCommands.WalletPassphrase, ct, new[]
                    {
                        extraPoolPaymentProcessingConfig.WalletPassword,
                        (object) 5 // unlock for N seconds
                    });

                    if(unlockResult.Error == null)
                    {
                        didUnlockWallet = true;
                        goto tryTransfer;
                    }

                    else
                        logger.Error(() => $"[{LogCategory}] {BitcoinCommands.WalletPassphrase} returned error: {result.Error.Message} code {result.Error.Code}");
                }

                else
                    logger.Error(() => $"[{LogCategory}] Wallet is locked but walletPassword was not configured. Unable to send funds.");
            }

            else
            {
                logger.Error(() => $"[{LogCategory}] {BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}");

                NotifyPayoutFailure(poolConfig.Id, balances, $"{BitcoinCommands.SendMany} returned error: {result.Error.Message} code {result.Error.Code}", null);
            }
        }
    }

    #endregion // IPayoutHandler
}
