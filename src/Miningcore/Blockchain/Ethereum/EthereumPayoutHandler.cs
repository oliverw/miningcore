using System.Data;
using System.Numerics;
using Autofac;
using AutoMapper;
using Miningcore.Blockchain.Ethereum.Configuration;
using Miningcore.Blockchain.Ethereum.DaemonRequests;
using Miningcore.Blockchain.Ethereum.DaemonResponses;
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
    private EthereumNetworkType networkType;
    private GethChainType chainType;
    private EthereumPoolConfigExtra extraPoolConfig;
    private EthereumPoolPaymentProcessingConfigExtra extraConfig;

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

        rpcClient = new RpcClient(pc.Daemons.First(x => string.IsNullOrEmpty(x.Category)), jsonSerializerSettings, messageBus, pc.Id);

        await DetectChainAsync(ct);
    }

    public async Task<Block[]> ClassifyBlocksAsync(IMiningPool pool, Block[] blocks, CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig);
        Contract.RequiresNonNull(blocks);

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
                        // There is a case scenario:
                        // - https://github.com/oliverw/miningcore/issues/1583#issuecomment-1383009149
                        // - https://github.com/oliverw/miningcore/discussions/1103#discussioncomment-2121240)
                        // Where a miner with enough hash-rate power is able to mine two blocks simultaneously with the same `height`
                        // The first block is always an uncle and the second block is a regular block
                        // We must handle that case carefully here, otherwise the PayoutManager will crash and no further blocks will be unlocked and no payment will be sent anymore.
                        
                        uint totalDuplicateBlock = await cf.Run(con => blockRepo.GetPoolDuplicateBlockCountByPoolHeightNoTypeAndStatusAsync(con, poolConfig.Id, Convert.ToInt64(block.BlockHeight), new[]
                        {
                            block.Status
                        }));
                        uint totalDuplicateBlockBefore = 0;
                        
                        if(totalDuplicateBlock > 1)
                        {
                            totalDuplicateBlockBefore = await cf.Run(con => blockRepo.GetPoolDuplicateBlockBeforeCountByPoolHeightNoTypeAndStatusAsync(con, poolConfig.Id, Convert.ToInt64(block.BlockHeight), new[]
                            {
                                block.Status
                            }, block.Created));
                        }
                        
                        if(totalDuplicateBlock > 1 && totalDuplicateBlockBefore < 1)
                        {
                            logger.Info(() => $"[{LogCategory}] Got {totalDuplicateBlock} `{block.Status}` blocks with the same blockHeight: {block.BlockHeight}");
                            
                            block.Reward = GetUncleReward(chainType, block.BlockHeight, block.BlockHeight);
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.BlockHeight = (ulong) blockInfo.Height;
                            block.Type = EthereumConstants.BlockTypeUncle;
                            
                            logger.Info(() => $"[{LogCategory}] Unlocked uncle at height {block.BlockHeight} worth {FormatAmount(block.Reward)}");
                            
                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        }
                        else {
                            var blockHashResponse = await rpcClient.ExecuteAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct,
                                new[] { (object) block.BlockHeight.ToStringHexWithPrefix(), true });
                            var blockHash = blockHashResponse.Response.Hash;
                            var baseGas = blockHashResponse.Response.BaseFeePerGas;
                            var gasUsed = blockHashResponse.Response.GasUsed;

                            var burnedFee = (decimal) 0;
                            if(extraPoolConfig?.ChainTypeOverride == "Ethereum" || extraPoolConfig?.ChainTypeOverride == "Main" || extraPoolConfig?.ChainTypeOverride == "MainPow" || extraPoolConfig?.ChainTypeOverride == "Ubiq" || extraPoolConfig?.ChainTypeOverride == "EtherOne" || extraPoolConfig?.ChainTypeOverride == "Pink")
                                burnedFee = (baseGas * gasUsed / EthereumConstants.Wei);

                            block.Hash = blockHash;
                            block.Status = BlockStatus.Confirmed;
                            block.ConfirmationProgress = 1;
                            block.BlockHeight = (ulong) blockInfo.Height;
                            block.Reward = GetBaseBlockReward(chainType, block.BlockHeight); // base reward
                            block.Type = EthereumConstants.BlockTypeBlock;

                            if(extraConfig?.KeepUncles == false)
                                block.Reward += blockInfo.Uncles.Length * (block.Reward / 32); // uncle rewards

                            if(extraConfig?.KeepTransactionFees == false && blockInfo.Transactions?.Length > 0)
                                block.Reward += await GetTxRewardAsync(blockInfo, ct) - burnedFee;

                            logger.Info(() => $"[{LogCategory}] Unlocked block {block.BlockHeight} worth {FormatAmount(block.Reward)}");

                            messageBus.NotifyBlockUnlocked(poolConfig.Id, block, coin);
                        }
                    }

                    continue;
                }

                // search for a block containing our block as an uncle by checking N blocks in either direction
                var heightMin = block.BlockHeight - extraConfig.BlockSearchOffset;
                var heightMax = Math.Min(block.BlockHeight + extraConfig.BlockSearchOffset, latestBlockHeight);
                
                logger.Debug(() => $"[{LogCategory}] BlockSearchOffset used: {extraConfig.BlockSearchOffset} - Range of blockHeight searched - Min: {heightMin}, Max: {heightMax}");
                
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
                            if(block.Reward == 0)
                                block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);

                            if(latestBlockHeight - uncle.Height.Value >= EthereumConstants.MinConfimations)
                            {

                                // make sure there is no other uncle from that block stored in the DB already.
                                // when there is more than 1 uncle mined by us within the BlockSearchOffset 
                                // range, the pool automatically assumes the first found block is the correct one. 
                                // This is not always the case, so we need to check the DB for any other 
                                // uncles from that block and continue searching if there any others.
                                // Otherwise the payouter will crash and no further blocks will be unlocked.
                                var uncleBlock = await cf.Run(con => blockRepo.GetBlockByPoolHeightAndTypeAsync(con, poolConfig.Id, Convert.ToInt64(uncle.Height.Value), EthereumConstants.BlockTypeUncle));
                                if(uncleBlock != null)
                                {
                                    logger.Info(() => $"[{LogCategory}] Found another uncle from block {uncle.Height.Value} in the DB. Continuing search for uncle.");
                                    continue;
                                }
                                
                                block.Hash = uncle.Hash;
                                block.Reward = GetUncleReward(chainType, uncle.Height.Value, blockInfo2.Height.Value);
                                block.Status = BlockStatus.Confirmed;
                                block.ConfirmationProgress = 1;
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

        if((networkType == EthereumNetworkType.Main || extraPoolConfig?.ChainTypeOverride == "Classic" || extraPoolConfig?.ChainTypeOverride == "Mordor" || networkType == EthereumNetworkType.MainPow || extraPoolConfig?.ChainTypeOverride == "Ubiq" || extraPoolConfig?.ChainTypeOverride == "EtherOne" || extraPoolConfig?.ChainTypeOverride == "Pink") &&
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

    public double AdjustBlockEffort(double effort)
    {
        return effort;
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
            case GethChainType.Main:
            case GethChainType.MainPow:
                if(height >= EthereumConstants.ConstantinopleHardForkHeight)
                    return EthereumConstants.ConstantinopleReward;
                if(height >= EthereumConstants.ByzantiumHardForkHeight)
                    return EthereumConstants.ByzantiumBlockReward;

                return EthereumConstants.HomesteadBlockReward;
            
            case GethChainType.Classic:
            case GethChainType.Mordor:
                // ETC block reward distribution - ECIP 1017 - https://ecips.ethereumclassic.org/ECIPs/ecip-1017
                double heightEraLength = height / EthereumClassicConstants.EraLength;
                double era = Math.Truncate(heightEraLength);
                decimal quotient = Convert.ToDecimal(Math.Pow(EthereumClassicConstants.DisinflationRateQuotient, era));
                decimal divisor = Convert.ToDecimal(Math.Pow(EthereumClassicConstants.DisinflationRateDivisor, era));
                return ((EthereumClassicConstants.BaseRewardInitial * quotient) / divisor);
            
            case GethChainType.EtherOne:
                return EthOneConstants.BaseRewardInitial;
            
            case GethChainType.Pink:
               return PinkConstants.BaseRewardInitial;

            case GethChainType.Callisto:
                return CallistoConstants.BaseRewardInitial * (CallistoConstants.TreasuryPercent / 100);
            
            case GethChainType.Ubiq:
                if(height >= UbiqConstants.OrionHardForkHeight)
                    return UbiqConstants.OrionBlockReward;
                if(height >= UbiqConstants.YearFourHeight)
                    return UbiqConstants.YearFourBlockReward;
                if(height >= UbiqConstants.YearThreeHeight)
                    return UbiqConstants.YearThreeBlockReward;
                if(height >= UbiqConstants.YearTwoHeight)
                    return UbiqConstants.YearTwoBlockReward;
                if(height >= UbiqConstants.YearOneHeight)
                    return UbiqConstants.YearOneBlockReward;
                
                return UbiqConstants.BaseRewardInitial;
            
            default:
                throw new Exception("Unable to determine block reward: Unsupported chain type");
        }
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
        
        switch(chainType)
        {
            case GethChainType.Classic:
            case GethChainType.Mordor:
                double heightEraLength = height / EthereumClassicConstants.EraLength;
                double era = Math.Floor(heightEraLength);
                if (era == 0) {
                    reward *= uheight + 8 - height;
                    reward /= 8m;
                }
                else {
                    reward /= 32m;
                }
                
                break;
            case GethChainType.Ubiq:
                reward *= uheight + 2 - height;
                reward /= 2m;
                // blocks older than the previous block are not rewarded
                if (reward < 0)
                    reward = 0;
                
                break;
            default:
                reward *= uheight + 8 - height;
                reward /= 8m;
                
                break;
        }
        
        return reward;
    }

    private async Task DetectChainAsync(CancellationToken ct)
    {
        var commands = new[]
        {
            new RpcRequest(EC.GetNetVersion),
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

        EthereumUtils.DetectNetworkAndChain(netVersion, gethChain, out networkType, out chainType);
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
        
        if(extraPoolConfig?.ChainTypeOverride == "Ethereum" || extraPoolConfig?.ChainTypeOverride == "Main" || (extraPoolConfig?.ChainTypeOverride == "Ubiq") || extraPoolConfig?.ChainTypeOverride == "MainPow" || extraPoolConfig?.ChainTypeOverride == "EtherOne" )
        {
            var maxPriorityFeePerGas = await rpcClient.ExecuteAsync<string>(logger, EC.MaxPriorityFeePerGas, ct);
            
            logger.Debug(() => $"[{LogCategory}] MaxPriorityFeePerGas: {maxPriorityFeePerGas.Response.IntegralFromHex<ulong>()} Wei");
            
            if(extraPoolConfig?.ChainTypeOverride == "Ubiq")
            {
                // get latest block
                var latestBlockResponse = await rpcClient.ExecuteAsync<DaemonResponses.Block>(logger, EC.GetBlockByNumber, ct, new[] { (object) "latest", true });
                var latestBlockHeight = latestBlockResponse.Response.Height.Value;

                // EIP-1559 Transaction
                if(latestBlockHeight >= UbiqConstants.OrionHardForkHeight)
                {
                    logger.Debug(() => $"[{LogCategory}] Transaction (EIP-1559)");
                    
                    request.Gas = extraConfig.Gas;
                    request.MaxPriorityFeePerGas = maxPriorityFeePerGas.Response.IntegralFromHex<ulong>();
                }
                // Legacy Transaction
                else {
                    logger.Debug(() => $"[{LogCategory}] Transaction (Legacy)");
                    
                    request.Gas = extraConfig.Gas;
                    request.GasPrice = extraConfig.MaxFeePerGas;
                }
            }
            else {
                request.Gas = extraConfig.Gas;
                request.MaxPriorityFeePerGas = maxPriorityFeePerGas.Response.IntegralFromHex<ulong>();
                request.MaxFeePerGas = extraConfig.MaxFeePerGas;
            }
        }

        RpcResponse<string> response;
        if(extraPoolConfig?.ChainTypeOverride == "Pink")
        {
            var requestPink = new SendTransactionRequestPink
            {
                From = poolConfig.Address,
                To = balance.Address,
                Value = amount.ToString("x").TrimStart('0'),
                Gas = extraConfig.Gas
            };
            response = await rpcClient.ExecuteAsync<string>(logger, EC.SendTx, ct, new[] { requestPink });
        }  
        else {
            response = await rpcClient.ExecuteAsync<string>(logger, EC.SendTx, ct, new[] { request });
        }

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
}
