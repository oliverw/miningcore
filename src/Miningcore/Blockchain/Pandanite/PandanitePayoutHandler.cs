using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Pandanite.Configuration;
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
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain.Pandanite;

[CoinFamily(CoinFamily.Pandanite)]
public class PandanitePayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public PandanitePayoutHandler(
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
    protected IPandaniteNodeApi Node;
    protected PandanitePoolConfigExtra extraPoolConfig;
    protected PandaniteDaemonEndpointConfigExtra extraPoolEndpointConfig;
    protected PandanitePoolPaymentProcessingConfigExtra extraPoolPaymentProcessingConfig;

    protected override string LogCategory => "Pandanite Payout Handler";

    #region IPayoutHandler

    public virtual Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        Contract.RequiresNonNull(pc);

        poolConfig = pc;
        clusterConfig = cc;

        extraPoolConfig = pc.Extra.SafeExtensionDataAs<PandanitePoolConfigExtra>();
        extraPoolEndpointConfig = pc.Extra.SafeExtensionDataAs<PandaniteDaemonEndpointConfigExtra>();
        extraPoolPaymentProcessingConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<PandanitePoolPaymentProcessingConfigExtra>();

        logger = LogUtil.GetPoolScopedLogger(typeof(PandanitePayoutHandler), pc);

        // TODO: implement failover??
        var daemon = poolConfig.Daemons.First();
        var httpClient = new HttpClient();
        Node = new PandaniteNodeV1Api(httpClient, string.Join(":", daemon.Host, daemon.Port));

        return Task.CompletedTask;
    }

    public virtual async Task<Persistence.Model.Block[]> ClassifyBlocksAsync(IMiningPool pool, Persistence.Model.Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

        var coin = poolConfig.Template.As<CoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var result = new List<Block>();
        var minConfirmations = extraPoolEndpointConfig?.MinimumConfirmations ?? 50;

        var chainLength = await Node.GetBlock();

        if(!chainLength.success) {
            logger.Error(() => $"[{LogCategory}] Daemon failed to load current chain height");
            return new Persistence.Model.Block[0];
        }

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // build command batch
            var batch = page.Select(block => block.TransactionConfirmationData).ToArray();

            // execute batch
            var results = await Node.VerifyTransactions(batch);

            if (!results.success) {
                logger.Error(() => $"[{LogCategory}] Daemon failed to verify transactions");
                return new Persistence.Model.Block[0];
            }

            foreach (var block in page)
            {
                if (!results.data.TryGetValue(block.TransactionConfirmationData, out var cmdResult)) {
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;
                    result.Add(block);

                    logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned");

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }

                switch (cmdResult) {
                    case "IN_CHAIN":
                        var confirmations = chainLength.block - block.BlockHeight;

                        if (confirmations >= (ulong)minConfirmations) {
                            // matured and spendable coinbase transaction
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.Reward = block.Reward;
                            result.Add(block);

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        } else {
                            // update progress
                            block.ConfirmationProgress = Math.Min(1.0d, (double) confirmations / minConfirmations);
                            block.Reward = block.Reward;
                            result.Add(block);

                            messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);
                            break;
                        }

                        break;
                    case "NOT_IN_CHAIN":
                        block.Status = BlockStatus.Orphaned;
                        block.Reward = 0;
                        result.Add(block);

                        logger.Info(() => $"[{LogCategory}] Block {block.BlockHeight} classified as orphaned due tx not in chain");

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        break;
                    default:
                        logger.Warn(() => $"[{LogCategory}] Daemon failed to load transaction status for transaction {block.TransactionConfirmationData}");
                        break;
                }
            }
        }

        return result.ToArray();
    }

    public virtual async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        Contract.RequiresNonNull(balances);

        // build args
        var amounts = balances
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Address, x => x.Amount);

        if(amounts.Count == 0)
            return;

        logger.Info(() => $"[{LogCategory}] Paying {FormatAmount(balances.Sum(x => x.Amount))} to {balances.Length} addresses");
        
        var txFailures = new List<Tuple<KeyValuePair<string, decimal>, Exception>>();
        var successBalances = new Dictionary<Balance, string>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = ct
        };

        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(amounts.Count / (double) pageSize);

        var timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = amounts
                .Skip(i * pageSize)
                .Take(pageSize)
                .Select(balance => {
                    var (address, amount) = balance;

                    var transaction = new Transaction
                    {
                        from = poolConfig.Address,
                        to = address,
                        amount = Convert.ToUInt64(Math.Floor(amount * 10000)),
                        fee = 1,
                        isTransactionFee = false,
                        timestamp = timestamp.ToString()
                    };

                    transaction.Sign(extraPoolConfig.PublicKey.ToByteArray(), extraPoolConfig.PrivateKey.ToByteArray());

                    return transaction;
                })
                .ToDictionary(tx => tx.CalculateContentHash().AsString(), tx => tx);

            var txList = page.Select(tx => {
                logger.Info(()=> $"[{LogCategory}] [{tx.Key}] Sending {FormatAmount(tx.Value.amount / 10000)} to {tx.Value.to}");
                return tx.Value;
            }).ToList();

            var (success, data) = await Node.SubmitTransactions(txList);

            if (!success) {
                throw new Exception("add_transaction_json failed with errors!");
            }

            foreach (var txStatus in data) {
                page.TryGetValue(txStatus.txid, out var tx);

                if (txStatus.status == "SUCCESS") {

                    successBalances.Add(new Balance
                    {
                        PoolId = poolConfig.Id,
                        Address = tx.to,
                        Amount = tx.amount / (decimal)10000,
                    }, txStatus.txid);

                    logger.Info(() => $"[{LogCategory}] Payment transaction id: {txStatus.txid}");
                } else {
                    var val = new KeyValuePair<string, decimal>(tx.to, tx.amount  / (decimal)10000);
                    txFailures.Add(Tuple.Create(val, new Exception($"Payment transaction id: {txStatus.txid} failed with error: {txStatus.status}")));
                }
            }
        }

        if(successBalances.Any())
        {
            await PersistPaymentsAsync(successBalances);

            NotifyPayoutSuccess(poolConfig.Id, successBalances.Keys.ToArray(), successBalances.Values.ToArray(), null);
        }

        if(txFailures.Any())
        {
            var failureBalances = txFailures.Select(x=> new Balance { Amount = x.Item1.Value }).ToArray();
            var error = string.Join(", ", txFailures.Select(x => $"{x.Item1.Key} {FormatAmount(x.Item1.Value)}: {x.Item2.Message}"));

            logger.Error(()=> $"[{LogCategory}] Failed to transfer the following balances: {error}");

            NotifyPayoutFailure(poolConfig.Id, failureBalances, error, null);
        }
    }

    public override double AdjustShareDifficulty(double difficulty)
    {
        return difficulty;
    }

    Task IPayoutHandler.ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    Task<Block[]> IPayoutHandler.ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    Task<decimal> IPayoutHandler.UpdateBlockRewardBalancesAsync(System.Data.IDbConnection con, System.Data.IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    Task IPayoutHandler.PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    double IPayoutHandler.AdjustShareDifficulty(double difficulty)
    {
        throw new NotImplementedException();
    }

    double IPayoutHandler.AdjustBlockEffort(double effort)
    {
        throw new NotImplementedException();
    }

    string IPayoutHandler.FormatAmount(decimal amount)
    {
        throw new NotImplementedException();
    }

    #endregion // IPayoutHandler
}
