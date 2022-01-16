using Autofac;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Blockchain;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using System.Collections.Concurrent;
using System.Globalization;

namespace Miningcore.Api.Controllers;

[Route("api")]
[ApiController]
public class ClusterApiController : ApiControllerBase
{
    public ClusterApiController(IComponentContext ctx) : base(ctx)
    {
        statsRepo = ctx.Resolve<IStatsRepository>();
        blocksRepo = ctx.Resolve<IBlockRepository>();
        paymentsRepo = ctx.Resolve<IPaymentRepository>();
        clock = ctx.Resolve<IMasterClock>();
        pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        enabledPools = new HashSet<string>(clusterConfig.Pools.Where(x => x.Enabled).Select(x => x.Id));
    }

    private readonly IStatsRepository statsRepo;
    private readonly IBlockRepository blocksRepo;
    private readonly IPaymentRepository paymentsRepo;
    private readonly IMasterClock clock;
    private readonly ConcurrentDictionary<string, IMiningPool> pools;
    private readonly HashSet<string> enabledPools;

    #region Actions

    [HttpGet("blocks")]
    public async Task<Responses.Block[]> PageBlocksPagedAsync(
        [FromQuery] int page, [FromQuery] int pageSize = 15, [FromQuery] BlockStatus[] state = null)
    {
        var ct = HttpContext.RequestAborted;
        var blockStates = state is { Length: > 0 } ?
            state :
            new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

        var blocks = (await cf.Run(con => blocksRepo.PageBlocksAsync(con, blockStates, page, pageSize, ct)))
            .Select(mapper.Map<Responses.Block>)
            .Where(x => enabledPools.Contains(x.PoolId))
            .ToArray();

        // enrich blocks
        var blocksByPool = blocks.GroupBy(x => x.PoolId);

        foreach(var poolBlocks in blocksByPool)
        {
            var pool = GetPoolNoThrow(poolBlocks.Key);

            if(pool == null)
                continue;

            var blockInfobaseDict = pool.Template.ExplorerBlockLinks;

            // compute infoLink
            if(blockInfobaseDict != null)
            {
                foreach(var block in poolBlocks)
                {
                    blockInfobaseDict.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl);

                    if(!string.IsNullOrEmpty(blockInfobaseUrl))
                    {
                        if(blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                        else if(blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                            block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                    }
                }
            }
        }

        return blocks;
    }

    #endregion // Actions
}
