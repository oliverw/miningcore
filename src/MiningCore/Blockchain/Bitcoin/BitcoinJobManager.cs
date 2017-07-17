using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using CodeContracts;
using Microsoft.Extensions.Logging;
using MiningCore.Blockchain.Bitcoin.Messages;
using MiningCore.Configuration;
using MiningCore.Stratum;

namespace MiningCore.Blockchain.Bitcoin
{
    public class BitcoinJobManager : JobManagerBase,
        IBlockchainJobManager
    {
        public BitcoinJobManager(
            IComponentContext ctx, 
            ILogger<BitcoinJobManager> logger,
            BlockchainDemon daemon,
            ExtraNonceProvider extraNonceProvider) : base(ctx, logger, daemon)
        {
            this.extraNonceProvider = extraNonceProvider;
        }

        private readonly ExtraNonceProvider extraNonceProvider;

        private readonly object blockTemplateLock = new object();
        private GetBlockTemplateResponse blockTemplate;

        #region API-Surface

        public async Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            var result = await daemon.ExecuteCmdAnyAsync<string[], ValidateAddressResponse>("validateaddress", new[] { address });

            return result.Response != null && result.Response.IsValid;
        }

        public Task<object[]> HandleWorkerSubscribeAsync(StratumClient worker)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            
            // setup worker context
            var job = new BitcoinWorkerContext
            {
                ExtraNonce1 = extraNonceProvider.Next().ToString("x4"),
            };

            worker.WorkerContext = job;

            // setup response data
            var responseData = new object[]
            {
                job.ExtraNonce1,
                extraNonceProvider.Size
            };

            return Task.FromResult(responseData);
        }

        public Task<bool> HandleWorkerSubmitAsync(StratumClient worker, object submission)
        {
            Contract.RequiresNonNull(worker, nameof(worker));
            Contract.RequiresNonNull(submission, nameof(submission));

            return Task.FromResult(true);
        }

        #endregion // API-Surface

        protected override async Task<bool> IsDaemonHealthy()
        {
            var responses = await daemon.ExecuteCmdAllAsync<GetInfoResponse>("getinfo");

            return responses.All(x => x.Error == null);
        }

        protected override async Task<bool> UpdateJobFromDaemon()
        {
            var result = await GetBlockTemplateAsync();

            lock (blockTemplateLock)
            {
                var isNew = blockTemplate == null || blockTemplate.PreviousBlockhash != result.PreviousBlockhash;

                if (isNew)
                    blockTemplate = result;

                return isNew;
            }
        }

        protected override object GetJobParamsForStratum()
        {
            return new object();
        }

        private async Task<GetBlockTemplateResponse> GetBlockTemplateAsync()
        {
            var result = await daemon.ExecuteCmdAnyAsync<object[], GetBlockTemplateResponse>("getblocktemplate",
                new object[]
                {
                    new
                    {
                        capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                        rules = new[] { "segwit" }
                    },
                });

            return result.Response;
        }
    }
}
