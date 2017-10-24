using System;
using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.Bitcoin.DaemonResponses;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Contracts;
using MiningCore.DaemonInterface;
using MiningCore.Time;

namespace MiningCore.Blockchain.ZCash
{
    public class ZCashJobManager : BitcoinJobManager<ZCashJob, ZCashBlockTemplate>
    {
        public ZCashJobManager(
			IComponentContext ctx, 
			IMasterClock clock, 
			BitcoinExtraNonceProvider extraNonceProvider) : base(ctx, clock, extraNonceProvider)
        {
            getBlockTemplateParams = new object[]
            {
                new
                {
                    capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                }
            };
        }

        public override async Task<bool> ValidateAddressAsync(string address)
        {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(address), $"{nameof(address)} must not be empty");

            // handle t-addr
            if (address.Length == 36)
                return await base.ValidateAddressAsync(address);

            // handle z-addr
            if (address.Length == 96)
            {
                var result = await daemon.ExecuteCmdAnyAsync<ValidateAddressResponse>(
                    ZCashCommands.ZValidateAddress, new[] {address});

                return result.Response != null && result.Response.IsValid;
            }

            return false;
        }

        protected override async Task<DaemonResponse<ZCashBlockTemplate>> GetBlockTemplateAsync()
        {
            var subsidyResponse = await daemon.ExecuteCmdAnyAsync<ZCashBlockSubsidy>(BitcoinCommands.GetBlockSubsidy);

            var result = await daemon.ExecuteCmdAnyAsync<ZCashBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            if (subsidyResponse.Error == null && result.Error == null && result.Response != null)
                result.Response.Subsidy = subsidyResponse.Response;

            return result;
        }
    }
}
