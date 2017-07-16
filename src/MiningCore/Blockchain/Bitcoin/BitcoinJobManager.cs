using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using MiningCore.Configuration;
using MiningCore.Configuration.Extensions;
using MiningCore.Extensions;
using MiningCore.MiningPool;
using MiningCore.Stratum;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinJobManager : IMiningJobManager
    {
        public BitcoinJobManager(IComponentContext ctx, ILogger<BitcoinJobManager> logger,
            ExtraNonceProvider extraNonceProvider)
        {
            this.ctx = ctx;
            this.logger = logger;
            this.extraNonceProvider = extraNonceProvider;
        }

        private readonly IComponentContext ctx;
        private readonly ILogger<BitcoinJobManager> logger;
        private readonly ExtraNonceProvider extraNonceProvider;

        #region API-Surface

        public Task StartAsync(PoolConfig poolConfig)
        {
            return Task.FromResult(true);
        }

        public void RegisterWorker(StratumClient worker)
        {
            var job = new BitcoinJobContext();
            //job.ExtraNonce1 = extraNonceProvider.Next();

            worker.MiningContext = job;
        }

        public Task<object> GetStratumSubscribeParamsAsync()
        {
            return Task.FromResult(new object());
        }

        #endregion // API-Surface
    }
}
