/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using Miningcore.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Miningcore.PoolCore
{
    internal class PoolCoinTemplates
    {

        internal static Dictionary<string, CoinTemplate> LoadCoinTemplates()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var defaultTemplates = Path.Combine(basePath, "coins.json");

            // make sure default templates are loaded first
            Pool.clusterConfig.CoinTemplates = new[]
            {
                defaultTemplates
            }
            .Concat(Pool.clusterConfig.CoinTemplates != null ?
                Pool.clusterConfig.CoinTemplates.Where(x => x != defaultTemplates) :
                new string[0])
            .ToArray();

            return CoinTemplateLoader.Load(Pool.container, Pool.clusterConfig.CoinTemplates);
        }



    }
}
