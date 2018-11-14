using Autofac;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api.Extensions;
using Miningcore.Api.Responses;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Model.Projections;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Miningcore.Api.Controllers
{
    [Route("api")]
    [ApiController]
    public class ClusterApiController : ControllerBase
    {
        public ClusterApiController(IComponentContext ctx)
        {
            clusterConfig = ctx.Resolve<ClusterConfig>();
            cf = ctx.Resolve<IConnectionFactory>();
            statsRepo = ctx.Resolve<IStatsRepository>();
            blocksRepo = ctx.Resolve<IBlockRepository>();
            paymentsRepo = ctx.Resolve<IPaymentRepository>();
            mapper = ctx.Resolve<IMapper>();
            clock = ctx.Resolve<IMasterClock>();
            pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
        }

        private readonly ClusterConfig clusterConfig;
        private readonly IConnectionFactory cf;
        private readonly IStatsRepository statsRepo;
        private readonly IBlockRepository blocksRepo;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IMapper mapper;
        private readonly IMasterClock clock;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;

        #region Actions

        [HttpGet("blocks")]
        public async Task<Responses.Block[]> PageBlocksPagedAsync(
            [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] BlockStatus[] state)
        {
            var enabledPools = new HashSet<string>(clusterConfig.Pools.Where(x => x.Enabled).Select(x=> x.Id));

            var blockStates = state != null && state.Length > 0 ?
                state :
                new[] { BlockStatus.Confirmed, BlockStatus.Pending, BlockStatus.Orphaned };

            var blocks = (await cf.Run(con => blocksRepo.PageBlocksAsync(con, blockStates, page, pageSize)))
                .Select(mapper.Map<Responses.Block>)
                .Where(x=> enabledPools.Contains(x.PoolId))
                .ToArray();

            // enrich blocks
            var blocksByPool = blocks.GroupBy(x => x.PoolId);

            foreach (var poolBlocks in blocksByPool)
            {
                var pool = GetPoolNoThrow(poolBlocks.Key);

                if (pool == null)
                    continue;

                var blockInfobaseDict = pool.Template.ExplorerBlockLinks;
                
                // compute infoLink
                if (blockInfobaseDict != null)
                {
                    foreach (var block in poolBlocks)
                    {
                        blockInfobaseDict.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl);

                        if (!string.IsNullOrEmpty(blockInfobaseUrl))
                        {
                            if (blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                                block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                            else if (blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                                block.InfoLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                        }
                    }
                }
            }

            return blocks;
        }

        #endregion // Actions

        private PoolConfig GetPoolNoThrow(string poolId)
        {
            if (string.IsNullOrEmpty(poolId))
                return null;

            var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);
            return pool;
        }
    }
}
