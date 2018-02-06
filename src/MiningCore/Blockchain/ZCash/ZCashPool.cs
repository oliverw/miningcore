/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using Autofac;
using AutoMapper;
using MiningCore.Blockchain.ZCash.Configuration;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using MiningCore.Util;
using Newtonsoft.Json;

namespace MiningCore.Blockchain.ZCash
{
    [CoinMetadata(CoinType.ZEC, CoinType.ZCL, CoinType.ZEN)]
    public class ZCashPool : ZCashPoolBase<ZCashJob>
    {
        public ZCashPool(IComponentContext ctx,
            JsonSerializerSettings serializerSettings,
            IConnectionFactory cf,
            IStatsRepository statsRepo,
            IMapper mapper,
            IMasterClock clock,
            NotificationService notificationService) :
            base(ctx, serializerSettings, cf, statsRepo, mapper, clock, notificationService)
        {
        }

        private ZCashPoolConfigExtra extraConfig;

        public override void Configure(PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            base.Configure(poolConfig, clusterConfig);

            extraConfig = poolConfig.Extra.SafeExtensionDataAs<ZCashPoolConfigExtra>();

            if (string.IsNullOrEmpty(extraConfig?.ZAddress))
                logger.ThrowLogPoolStartupException($"Pool z-address is not configured", LogCat);
        }
    }
}
