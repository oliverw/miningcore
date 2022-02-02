using System.Collections.Concurrent;
using System.Data;
using System.Numerics;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Ethereum.Configuration;
using Miningcore.Blockchain.Ethereum.DaemonRequests;
using Miningcore.Blockchain.Ethereum.DaemonResponses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.JsonRpc;
using Miningcore.Messaging;
using Miningcore.Mining;
using Miningcore.Notifications.Messages;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Rpc;
using Miningcore.Time;
using Miningcore.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using Block = Miningcore.Persistence.Model.Block;
using Contract = Miningcore.Contracts.Contract;
using EC = Miningcore.Blockchain.Ethereum.EthCommands;

namespace Miningcore.Blockchain.Ethereum;

[CoinFamily(CoinFamily.Ethereum)]
public class EthereumPayoutHandler : PayoutHandlerBase,
    IPayoutHandler
{
    public EthereumPayoutHandler(
        IComponentContext ctx,
        IConnectionFactory cf,
        IMapper mapper,
        IShareRepository shareRepo,
        IBlockRepository blockRepo,
        IBalanceRepository balanceRepo,
        IPaymentRepository paymentRepo,
        IMasterClock clock,
        IMessageBus messageBus,
        EthereumJobManager ethereumJobManager) :
        base(cf, mapper, shareRepo, blockRepo, balanceRepo, paymentRepo, clock, messageBus)
    {
        Contract.RequiresNonNull(ctx, nameof(ctx));
        Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
        Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));

        this.ctx = ctx;

        this.ethereumJobManager = ethereumJobManager;
    }

    private const int BlockSearchOffset = 50;
    private const decimal MaxPayout = 0.1m; // ethereum

    private readonly IComponentContext ctx;
    private RpcClient rpcClient;
    private EthereumNetworkType networkType;
    private GethChainType chainType;
    private BigInteger chainId;
    private EthereumJobManager ethereumJobManager;
    private EthereumPoolConfigExtra extraPoolConfig;
    private EthereumPoolPaymentProcessingConfigExtra extraConfig;
    private IWeb3 web3Connection;
    private Task ondemandPayTask;
    private static ConcurrentDictionary<string, string> transactionHashes = new();

    protected override string LogCategory => "Ethereum Payout Handler";

    #region IPayoutHandler

    public async Task ConfigureAsync(ClusterConfig cc, PoolConfig pc, CancellationToken ct)
    {
        poolConfig = pc;
        clusterConfig = cc;
        extraPoolConfig = pc.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>();
        extraConfig = pc.PaymentProcessing.Extra.SafeExtensionDataAs<EthereumPoolPaymentProcessingConfigExtra>();

        logger = LogUtil.GetPoolScopedLogger(typeof(EthereumPayoutHandler), pc);

        // configure standard daemon
        var jsonSerializerSettings = ctx.Resolve<JsonSerializerSettings>();

        var daemonEndpointConfig = pc.Daemons.First(x => string.IsNullOrEmpty(x.Category));
        rpcClient = new RpcClient(daemonEndpointConfig, jsonSerializerSettings, messageBus, pc.Id);

        await DetectChainAsync(ct);

        // if pKey is configured - setup web3 connection for self managed wallet payouts
        InitializeWeb3(daemonEndpointConfig);
    }

    private void InitializeWeb3(DaemonEndpointConfig daemonConfig)
    {
        if(string.IsNullOrEmpty(extraConfig.PrivateKey)) return;

        var txEndpoint = daemonConfig;
        var protocol = (txEndpoint.Ssl || txEndpoint.Http2) ? "https" : "http";
        var txEndpointUrl = $"{protocol}://{txEndpoint.Host}:{txEndpoint.Port}";

        var account = chainId != 0 ? new Account(extraConfig.PrivateKey, chainId) : new Account(extraConfig.PrivateKey);
        web3Connection = new Web3(account, txEndpointUrl);
    }

    public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig, nameof(poolConfig));
        Contract.RequiresNonNull(blocks, nameof(blocks));

        var coin = poolConfig.Template.As<EthereumCoinTemplate>();
        var pageSize = 100;
        var pageCount = (int) Math.Ceiling(blocks.Length / (double) pageSize);
        var blockCache = new Dictionary<long, DaemonResponses.Block>();
        var result = new List<Block>();

        for(var i = 0; i < pageCount; i++)
        {
            // get a page full of blocks
            var page = blocks
                .Skip(i * pageSize)
                .Take(pageSize)
                .ToArray();

            // get latest block
            var latestBlockResponse = await rpcClient.ExecuteAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) "latest", true });
            var latestBlockHeight = latestBlockResponse.Response.Height.Value;

            // execute batch
            var blockInfos = await FetchBlocks(blockCache, ct, page.Select(block => (long) block.BlockHeight).ToArray());

            for(var j = 0; j < blockInfos.Length; j++)
            {
                var blockInfo = blockInfos[j];
                var block = page[j];

                // update progress
                block.ConfirmationProgress = Math.Min(1.0d, (double) (latestBlockHeight - block.BlockHeight) / EthereumConstants.MinConfimations);
                result.Add(block);

                messageBus.NotifyBlockConfirmationProgress(poolConfig.Id, block, coin);

                // is it block mined by us?
                if(string.Equals(blockInfo.Miner, poolConfig.Address, StringComparison.OrdinalIgnoreCase))
                {
                    // mature?
                    if(latestBlockHeight - block.BlockHeight >= EthereumConstants.MinConfimations)
                    {
                        var blockHashResponse = await rpcClient.ExecuteAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct,
                            new[] { (object) block.BlockHeight.ToStringHexWithPrefix(), true });
                        var blockHash = blockHashResponse.Response.Hash;
                        var baseGas = blockHashResponse.Response.BaseFeePerGas;
                        var gasUsed = blockHashResponse.Response.GasUsed;

                        var burnedFee = (decimal) 0;
                        if(extraPoolConfig?.ChainTypeOverride == "Ethereum")
                            burnedFee = (baseGas * gasUsed / EthereumConstants.Wei);

                        block.Hash = blockHash;
                        block.Status = BlockStatus.Confirmed;
                        block.ConfirmationProgress = 1;
                        block.BlockHeight = (ulong) blockInfo.Height;
                        block.Reward = GetBaseBlockReward(chainType, block.BlockHeight); // base reward
                        block.Type = "block";

                        if(extraConfig?.KeepUncles == false)
                            block.Reward += blockInfo.Uncles.Length * (block.Reward / 32); // uncle rewards

                        if(extraConfig?.KeepTransactionFees == false && blockInfo.Transactions?.Length > 0)
                            block.Reward += await GetTxRewardAsync(blockInfo, ct) - burnedFee;

                        logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                        messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                    }

                    continue;
                }

                // search for a block containing our block as an uncle by checking N blocks in either direction
                var heightMin = block.BlockHeight - BlockSearchOffset;
                var heightMax = Math.Min(block.BlockHeight + BlockSearchOffset, latestBlockHeight);
                var range = new List<long>();

                for(var k = heightMin; k < heightMax; k++)
                    range.Add((long) k);

                // execute batch
                var blockInfo2s = await FetchBlocks(blockCache, ct, range.ToArray());

                foreach(var blockInfo2 in blockInfo2s)
                {
                    // don't give up yet, there might be an uncle
                    if(blockInfo2.Uncles.Length > 0)
                    {
                        // fetch all uncles in a single RPC batch request
                        var uncleBatch = blockInfo2.Uncles.Select((x, index) => new RpcRequest(EC.GetUncleByBlockNumberAndIndex,
                                new[] { blockInfo2.Height.Value.ToStringHexWithPrefix(), index.ToStringHexWithPrefix() }))
                            .ToArray();

                        logger.Info(() => $"[{LogCategory}] Fetching {blockInfo2.Uncles.Length} uncles for block {blockInfo2.Height}");

                        var uncleResponses = await rpcClient.ExecuteBatchAsync(logger, ct, uncleBatch);

                        logger.Info(() => $"[{LogCategory}] Fetched {uncleResponses.Count(x => x.Error == null && x.Response != null)} uncles for block {blockInfo2.Height}");

                        var uncle = uncleResponses.Where(x => x.Error == null && x.Response != null)
                            .Select(x => x.Response.ToObject<DaemonResponses.Block>())
                            .FirstOrDefault(x => string.Equals(x.Miner, poolConfig.Address, StringComparison.OrdinalIgnoreCase));

                        if(uncle != null)
                        {
                            // mature?
                            if(latestBlockHeight - uncle.Height.Value >= EthereumConstants.MinConfimations)
                            {
                                var blockHashUncleResponse = await rpcClient.ExecuteAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct,
                                    new[] { (object) uncle.Height.Value.ToStringHexWithPrefix(), true });
                                var blockHashUncle = blockHashUncleResponse.Response.Hash;

                                block.Hash = blockHashUncle;
                                block.Status = BlockStatus.Confirmed;
                                block.ConfirmationProgress = 1;
                                block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);
                                block.BlockHeight = uncle.Height.Value;
                                block.Type = EthereumConstants.BlockTypeUncle;

                                logger.Info(() => $"[{LogCategory}] Unlocked uncle for block {blockInfo2.Height.Value} at height {uncle.Height.Value} worth {FormatAmount(block.Reward)}");

                                messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                            }

                            else
                                logger.Info(() => $"[{LogCategory}] Got immature matching uncle for block {blockInfo2.Height.Value}. Will try again.");

                            break;
                        }
                    }
                }

                if(block.Status == BlockStatus.Pending && block.ConfirmationProgress > 0.75)
                {
                    // we've lost this one
                    block.Hash = "0x0";
                    block.Status = BlockStatus.Orphaned;
                    block.Reward = 0;

                    messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                }
            }
        }

        return result.ToArray();
    }

    public Task CalculateBlockEffortAsync(IMiningPool pool, Block block, double accumulatedBlockShareDiff, CancellationToken ct)
    {
        block.Effort = accumulatedBlockShareDiff / block.NetworkDifficulty;

        return Task.FromResult(true);
    }

    public override async Task<decimal> UpdateBlockRewardBalancesAsync(IDbConnection con, IDbTransaction tx, IMiningPool pool, Block block, CancellationToken ct)
    {
        var blockRewardRemaining = await base.UpdateBlockRewardBalancesAsync(con, tx, pool, block, ct);

        // Deduct static reserve for tx fees
        blockRewardRemaining -= EthereumConstants.StaticTransactionFeeReserve;

        return blockRewardRemaining;
    }

    public async Task PayoutAsync(IMiningPool pool, Balance[] balances, CancellationToken ct)
    {
        // ensure we have peers
        var infoResponse = await rpcClient.ExecuteAsync<string>(logger, EC.GetPeerCount, ct);

        if(networkType == EthereumNetworkType.Mainnet &&
           (infoResponse.Error != null || string.IsNullOrEmpty(infoResponse.Response) ||
               infoResponse.Response.IntegralFromHex<int>() < EthereumConstants.MinPayoutPeerCount))
        {
            logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
            return;
        }

        var txHashes = new List<string>();

        foreach(var balance in balances)
        {
            try
            {
                var txHash = await PayoutAsync(balance, ct);
                txHashes.Add(txHash);
            }

            catch(Exception ex)
            {
                logger.Error(ex);

                NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
            }
        }

        if(txHashes.Any())
            NotifyPayoutSuccess(poolConfig.Id, balances, txHashes.ToArray(), null);
    }

    public async Task<PayoutReceipt> PayoutAsync(Balance balance)
    {
        if(balance.Amount.CompareTo(MaxPayout) > 0)
        {
            logger.Error(() => $"[{LogCategory}] Aborting payout of more than maximum in a single transaction amount: {balance.Amount} wallet {balance.Address}");
            throw new Exception("Aborting payout over maximum amount");
        }

        PayoutReceipt receipt;

        // If web3Connection was created, payout from self managed wallet
        if(web3Connection != null)
        {
            receipt = await PayoutWebAsync(balance);
        }
        else // else payout from daemon managed wallet
        {
            if(!string.IsNullOrEmpty(extraConfig.PrivateKey))
            {
                logger.Error(() => $"[{LogCategory}] Web3 is configured, but web3Connection is null!");
                throw new Exception($"Unable to process payouts because web3 is null");
            }

            receipt = new PayoutReceipt { Id = await PayoutAsync(balance, CancellationToken.None) };
        }

        if(receipt != null)
        {
            logger.Info(() => $"[{LogCategory}] Payout transaction id: {receipt.Id}");
        }

        return receipt;
    }

    public async Task ConfigureOnDemandPayoutAsync(CancellationToken ct)
    {
        messageBus.Listen<NetworkBlockNotification>().Subscribe(block =>
        {
            logger.Info($"[{LogCategory}] NetworkBlockNotification height={block.BlockHeight}, gasfee={block.BaseFeePerGas}");

            // Handle an invalid gas fee
            if(block.BaseFeePerGas <= 0)
            {
                logger.Warn($"[{LogCategory}] NetworkBlockNotification invalid gas fee value, gasfee={block.BaseFeePerGas}");
                return;
            }

            // Check if we are over our hard limit
            if(block.BaseFeePerGas > extraConfig.MaxGasLimit)
            {
                logger.Info($"[{LogCategory}] Gas exceeds the MaxGasLimit={extraConfig.MaxGasLimit}, skipping payouts");
                return;
            }

            var minimumPayout = GetMinimumPayout(poolConfig.PaymentProcessing.MinimumPayment, block.BaseFeePerGas, extraConfig.GasDeductionPercentage, extraConfig.Gas);

            // Trigger payouts
            if(ondemandPayTask == null || ondemandPayTask.IsCompleted)
            {
                logger.Info($"[{LogCategory}] Triggering a new on-demand payouts since gas is below {extraConfig.MaxGasLimit}, gasfee={block.BaseFeePerGas}, minimumPayout={minimumPayout}");
                ondemandPayTask = PayoutBalancesOverThresholdAsync(minimumPayout);
            }
            else
            {
                logger.Info($"[{LogCategory}] Existing on-demand payouts is still processing, gasfee={block.BaseFeePerGas}");
            }
        });
    }

    public async Task<decimal> GetWalletBalance()
    {
        if(web3Connection == null) return 0;

        try
        {
            var balance = await web3Connection.Eth.GetBalance.SendRequestAsync(poolConfig.Address);
            return Web3.Convert.FromWei(balance.Value);
        }
        catch(Exception ex)
        {
            logger.Error(ex, "Error while fetching wallet balance");
            return 0;
        }
    }

    public decimal GetTransactionDeduction(decimal amount)
    {
        if(extraConfig.GasDeductionPercentage < 0 || extraConfig.GasDeductionPercentage > 100)
        {
            throw new Exception($"Invalid GasDeductionPercentage: {extraConfig.GasDeductionPercentage}");
        }

        return amount * extraConfig.GasDeductionPercentage / 100;
    }

    public bool MinersPayTxFees()
    {
        return extraConfig?.MinersPayTxFees == true;
    }

    #endregion // IPayoutHandler

    private async Task<DaemonResponses.Block[]> FetchBlocks(Dictionary<long, DaemonResponses.Block> blockCache, CancellationToken ct, params long[] blockHeights)
    {
        var cacheMisses = blockHeights.Where(x => !blockCache.ContainsKey(x)).ToArray();

        if(cacheMisses.Any())
        {
            var blockBatch = cacheMisses.Select(height => new RpcRequest(EC.GetBlockByNumber,
                new[]
                {
                    (object) height.ToStringHexWithPrefix(),
                    true
                })).ToArray();

            var tmp = await rpcClient.ExecuteBatchAsync(logger, ct, blockBatch);

            var transformed = tmp
                .Where(x => x.Error == null && x.Response != null)
                .Select(x => x.Response?.ToObject<DaemonResponses.Block>())
                .Where(x => x != null)
                .ToArray();

            foreach(var block in transformed)
                blockCache[(long) block.Height.Value] = block;
        }

        return blockHeights.Select(x => blockCache[x]).ToArray();
    }

    internal static decimal GetBaseBlockReward(GethChainType chainType, ulong height)
    {
        switch(chainType)
        {
            case GethChainType.Ethereum:
                if(height >= EthereumConstants.ConstantinopleHardForkHeight)
                    return EthereumConstants.ConstantinopleReward;

                if(height >= EthereumConstants.ByzantiumHardForkHeight)
                    return EthereumConstants.ByzantiumBlockReward;

                return EthereumConstants.HomesteadBlockReward;

            case GethChainType.Callisto:
                return CallistoConstants.BaseRewardInitial * (CallistoConstants.TreasuryPercent / 100);

            default:
                throw new Exception("Unable to determine block reward: Unsupported chain type");
        }
    }

    internal static decimal GetMinimumPayout(decimal defaultMinimumPayout, ulong baseFeePerGas, decimal gasDeductionPercentage, ulong transferGas)
    {
        // The default minimum payout is based on the configuration
        var minimumPayout = defaultMinimumPayout;

        // If the GasDeductionPercentage is set, use a ratio instead
        if(gasDeductionPercentage > 0)
        {
            var latestGasFee = baseFeePerGas / EthereumConstants.Wei;
            if(latestGasFee <= 0)
            {
                throw new Exception($"LatestGasFee is invalid: {latestGasFee}");
            }

            var transactionCost = transferGas * latestGasFee;
            // preSkimPayout = transactionCost / deductionPercentage
            // minimumPayout = preSkimPayout - transactionCost
            // minimumPayout = transactionCost / deductionPercentage - transactionCost

            // alternatively:
            // minimumPayout = (transactionCost / deductionPercentage) * (1 - deductionPercentage)
            // => minimumPayout = transactionCost / deductionPercentage - transactionCost
            minimumPayout = transactionCost / (gasDeductionPercentage / 100) - transactionCost;
        }

        return minimumPayout;
    }

    private async Task<decimal> GetTxRewardAsync(DaemonResponses.Block blockInfo, CancellationToken ct)
    {
        // fetch all tx receipts in a single RPC batch request
        var batch = blockInfo.Transactions.Select(tx => new RpcRequest(EC.GetTxReceipt, new[] { tx.Hash }))
            .ToArray();

        var results = await rpcClient.ExecuteBatchAsync(logger, ct, batch);

        if(results.Any(x => x.Error != null))
            throw new Exception($"Error fetching tx receipts: {string.Join(", ", results.Where(x => x.Error != null).Select(y => y.Error.Message))}");

        // create lookup table
        var gasUsed = results.Select(x => x.Response.ToObject<TransactionReceipt>())
            .ToDictionary(x => x.TransactionHash, x => x.GasUsed);

        // accumulate
        var result = blockInfo.Transactions.Sum(x => (ulong) gasUsed[x.Hash] * ((decimal) x.GasPrice / EthereumConstants.Wei));

        return result;
    }

    internal static decimal GetUncleReward(GethChainType chainType, ulong uheight, ulong height)
    {
        var reward = GetBaseBlockReward(chainType, height);

        reward *= uheight + 8 - height;
        reward /= 8m;

        return reward;
    }

    private async Task DetectChainAsync(CancellationToken ct)
    {
        var commands = new[]
        {
            new RpcRequest(EC.GetNetVersion),
            new RpcRequest(EC.ChainId),
        };

        var results = await rpcClient.ExecuteBatchAsync(logger, ct, commands);

        if(results.Any(x => x.Error != null))
        {
            var errors = results.Take(1).Where(x => x.Error != null)
                .ToArray();

            if(errors.Any())
                throw new Exception($"Chain detection failed: {string.Join(", ", errors.Select(y => y.Error.Message))}");
        }

        // convert network
        var netVersion = results[0].Response.ToObject<string>();
        var gethChain = extraPoolConfig?.ChainTypeOverride ?? "Ethereum";
        var chainIdResult = results[1]?.Response?.ToObject<string>();

        EthereumUtils.DetectNetworkAndChain(netVersion, gethChain, chainIdResult ?? "0", out networkType, out chainType, out chainId);
    }

    private async Task<string> PayoutAsync(Balance balance, CancellationToken ct)
    {
        // send transaction
        logger.Info(() => $"[{LogCategory}] Sending {FormatAmount(balance.Amount)} to {balance.Address}");

        var amount = (BigInteger) Math.Floor(balance.Amount * EthereumConstants.Wei);

        var request = new SendTransactionRequest
        {
            From = poolConfig.Address,
            To = balance.Address,
            Value = amount.ToString("x").TrimStart('0'),
        };

        if(extraPoolConfig?.ChainTypeOverride == "Ethereum")
        {
            var maxPriorityFeePerGas = await rpcClient.ExecuteAsync<string>(logger, EC.MaxPriorityFeePerGas, ct);
            request.Gas = extraConfig.Gas;
            request.MaxPriorityFeePerGas = maxPriorityFeePerGas.Response.IntegralFromHex<ulong>();
            request.MaxFeePerGas = extraConfig.MaxFeePerGas;
        }

        var response = await rpcClient.ExecuteAsync<string>(logger, EC.SendTx, ct, new[] { request });

        if(response.Error != null)
            throw new Exception($"{EC.SendTx} returned error: {response.Error.Message} code {response.Error.Code}");

        if(string.IsNullOrEmpty(response.Response) || EthereumConstants.ZeroHashPattern.IsMatch(response.Response))
            throw new Exception($"{EC.SendTx} did not return a valid transaction hash");

        var txHash = response.Response;
        logger.Info(() => $"[{LogCategory}] Payment transaction id: {txHash}");

        // update db
        await PersistPaymentsAsync(new[] { balance }, txHash);

        // done
        return txHash;
    }

    private async Task<PayoutReceipt> PayoutWebAsync(Balance balance)
    {
        var txCts = new CancellationTokenSource();
        var txHash = string.Empty;
        try
        {
            logger.Info($"[{LogCategory}] Web3Tx start. addr={balance.Address},amt={balance.Amount}");

            var txService = web3Connection.Eth;
            if(txService != null)
            {
                BigInteger? nonce = null;
                // Use existing nonce to avoid duplicate transaction
                Nethereum.RPC.Eth.DTOs.TransactionReceipt txReceipt;
                if(transactionHashes.TryGetValue(balance.Address, out var prevTxHash))
                {
                    // Check if existing transaction was succeeded
                    txReceipt = await TelemetryUtil.TrackDependency(() => txService.Transactions.GetTransactionReceipt.SendRequestAsync(prevTxHash),
                        DependencyType.Web3, "GetTransactionReceipt", $"addr={balance.Address},amt={balance.Amount.ToStr()},txhash={prevTxHash}");

                    if(txReceipt != null)
                    {
                        if(txReceipt.HasErrors().GetValueOrDefault())
                        {
                            logger.Error($"[{LogCategory}] Web3Tx failed without a receipt. addr={balance.Address},amt={balance.Amount.ToStr()},status={txReceipt.Status}");
                            return null;
                        }
                        logger.Info($"[{LogCategory}] Web3Tx receipt found for existing tx. addr={balance.Address},amt={balance.Amount.ToStr()},txhash={prevTxHash},gasfee={txReceipt.EffectiveGasPrice}");

                        transactionHashes.TryRemove(balance.Address, out _);

                        await PersistPaymentsAsync(new[] { balance }, txReceipt.TransactionHash);

                        return new PayoutReceipt
                        {
                            Id = txReceipt.TransactionHash,
                            Fees = Web3.Convert.FromWei(txReceipt.EffectiveGasPrice),
                            Fees2 = Web3.Convert.FromWei(txReceipt.GasUsed)
                        };
                    }
                    logger.Info($"[{LogCategory}] Web3Tx fetching nonce. addr={balance.Address},amt={balance.Amount},txhash={prevTxHash}");
                    // Get nonce for existing transaction
                    var prevTx = await TelemetryUtil.TrackDependency(() => txService.Transactions.GetTransactionByHash.SendRequestAsync(prevTxHash),
                        DependencyType.Web3, "GetTransactionByHash", $"addr={balance.Address},amt={balance.Amount.ToStr()},txhash={prevTxHash}");
                    nonce = prevTx?.Nonce?.Value;
                    logger.Info($"[{LogCategory}] Web3Tx receipt not found for existing tx. addr={balance.Address},amt={balance.Amount},txhash={prevTxHash},nonce={nonce}");
                }

                // Queue transaction
                txHash = await TelemetryUtil.TrackDependency(() => txService.GetEtherTransferService().TransferEtherAsync(balance.Address, balance.Amount, nonce: nonce),
                    DependencyType.Web3, "TransferEtherAsync", $"addr={balance.Address},amt={balance.Amount.ToStr()},nonce={nonce}");

                if(string.IsNullOrEmpty(txHash) || EthereumConstants.ZeroHashPattern.IsMatch(txHash))
                {
                    logger.Error($"[{LogCategory}] Web3Tx failed without a valid transaction hash. addr={balance.Address},amt={balance.Amount.ToStr()}");
                    return null;
                }
                logger.Info($"[{LogCategory}] Web3Tx queued. addr={balance.Address},amt={balance.Amount.ToStr()},txhash={txHash}");

                // Wait for transaction receipt
                txCts.CancelAfter(TimeSpan.FromMinutes(5)); // Timeout tx after 5min
                txReceipt = await TelemetryUtil.TrackDependency(() => txService.TransactionManager.TransactionReceiptService.PollForReceiptAsync(txHash, txCts),
                    DependencyType.Web3, "PollForReceiptAsync", $"addr={balance.Address},amt={balance.Amount.ToStr()},txhash={txHash}");
                if(txReceipt.HasErrors().GetValueOrDefault())
                {
                    logger.Error($"[{LogCategory}] Web3Tx failed without a receipt. addr={balance.Address},amt={balance.Amount.ToStr()},status={txReceipt.Status},txhash={txHash}");
                    return null;
                }
                logger.Info($"[{LogCategory}] Web3Tx receipt received. addr={balance.Address},amt={balance.Amount},txhash={txHash},gasfee={txReceipt.EffectiveGasPrice}");
                // Release address from pending list if successfully paid out
                if(transactionHashes.ContainsKey(balance.Address)) transactionHashes.TryRemove(balance.Address, out _);

                await PersistPaymentsAsync(new[] { balance }, txReceipt.TransactionHash);

                return new PayoutReceipt
                {
                    Id = txReceipt.TransactionHash,
                    Fees = Web3.Convert.FromWei(txReceipt.EffectiveGasPrice),
                    Fees2 = Web3.Convert.FromWei(txReceipt.GasUsed)
                };
            }

            logger.Warn($"[{LogCategory}] Web3Tx GetEtherTransferService is null. addr={balance.Address}, amt={balance.Amount}");
        }
        catch(OperationCanceledException)
        {
            if(!string.IsNullOrEmpty(txHash))
            {
                transactionHashes.AddOrUpdate(balance.Address, txHash, (_, _) => txHash);
            }
            logger.Warn($"[{LogCategory}] Web3Tx transaction timed out. addr={balance.Address},amt={balance.Amount.ToStr()},txhash={txHash},pendingTxs={transactionHashes.Count}");
        }
        catch(Nethereum.JsonRpc.Client.RpcResponseException ex)
        {
            logger.Error(ex, $"[{LogCategory}] Web3Tx failed. {ex.Message}");
        }
        finally
        {
            txCts.Dispose();
        }

        return null;
    }

    private async Task PayoutBalancesOverThresholdAsync(decimal minimumPayout)
    {
        logger.Info(() => $"[{LogCategory}] Processing payout for pool [{poolConfig.Id}]");

        try
        {
            // Get the balances over the dynamically calculated threshold
            var poolBalancesOverMinimum = await TelemetryUtil.TrackDependency(
                () => cf.Run(con => balanceRepo.GetPoolBalancesOverThresholdAsync(con, poolConfig.Id, minimumPayout, extraConfig.PayoutBatchSize)),
                DependencyType.Sql,
                "GetPoolBalancesOverThresholdAsync",
                $"minimumPayout={minimumPayout}");

            // Payout the balances above the threshold
            if(poolBalancesOverMinimum.Length > 0)
            {
                await TelemetryUtil.TrackDependency(
                    () => PayoutBatchAsync(poolBalancesOverMinimum, minimumPayout),
                    DependencyType.Sql,
                    "PayoutBalancesOverThresholdAsync",
                    $"miners:{poolBalancesOverMinimum.Length},minimumPayout={minimumPayout}");
            }
            else
            {
                logger.Info(() => $"[{LogCategory}] No balances over calculated minimum payout {minimumPayout.ToStr()} for pool {poolConfig.Id}");
            }
        }
        catch(Exception ex)
        {
            logger.Error(ex, $"[{LogCategory}] Error while processing payout balances over threshold");
        }
    }

    private async Task PayoutBatchAsync(Balance[] balances, decimal minimumPayout)
    {
        logger.Info(() => $"[{LogCategory}] Beginning payout to top {extraConfig.PayoutBatchSize} miners.");

        // Ensure we have peers
        if(networkType == EthereumNetworkType.Mainnet && ethereumJobManager.BlockchainStats.ConnectedPeers < EthereumConstants.MinPayoutPeerCount)
        {
            logger.Warn(() => $"[{LogCategory}] Payout aborted. Not enough peers (4 required)");
            return;
        }

        // Init web3
        var daemonEndpointConfig = poolConfig.Daemons.First(x => string.IsNullOrEmpty(x.Category));
        if(!string.IsNullOrEmpty(extraConfig.PrivateKey) && web3Connection == null) 
        {
            InitializeWeb3(daemonEndpointConfig);
        }

        var txHashes = new Dictionary<PayoutReceipt, Balance>();
        var payTasks = new List<Task>(balances.Length);

        foreach(var balance in balances)
        {
            var balanceOverThreshold = balance.Amount - minimumPayout;
            if(balanceOverThreshold > 0)
            {
                decimal gasFeesOvercharged = (balanceOverThreshold / (1 - (extraConfig.GasDeductionPercentage / 100))) - balanceOverThreshold;
                logger.Info(() => $"[{LogCategory}] miner:{balance.Address}, amount:{balance.Amount}, minimumPayout:{minimumPayout}, gasDeduction:{extraConfig.GasDeductionPercentage}, overcharge:{gasFeesOvercharged}");
            }

            payTasks.Add(Task.Run(async () =>
            {
                var logInfo = $",addr={balance.Address},amt={balance.Amount.ToStr()}";
                try
                {
                    var receipt = await PayoutAsync(balance);
                    if(receipt != null)
                    {
                        lock(txHashes)
                        {
                            txHashes.Add(receipt, balance);
                        }
                    }
                }
                catch(Nethereum.JsonRpc.Client.RpcResponseException ex)
                {
                    if(ex.Message.Contains("Insufficient funds", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Warn($"[{LogCategory}] {ex.Message}{logInfo}");
                    }
                    else
                    {
                        logger.Error(ex, $"[{LogCategory}] {ex.Message}{logInfo}");
                    }

                    NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
                }
                catch(Exception ex)
                {
                    logger.Error(ex, $"[{LogCategory}] {ex.Message}{logInfo}");
                    NotifyPayoutFailure(poolConfig.Id, new[] { balance }, ex.Message, null);
                }
            }));
        }
        // Wait for all payment to finish
        await Task.WhenAll(payTasks);

        if(txHashes.Any()) NotifyPayoutSuccess(poolConfig.Id, txHashes, null);
        // Reset web3 when transactions are failing
        if(txHashes.Count < balances.Length) web3Connection = null;

        logger.Info(() => $"[{LogCategory}] Payouts complete.  Successfully processed top {txHashes.Count} of {balances.Length} payouts.");
    }
}
