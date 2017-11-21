using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.DaemonInterface;
using MiningCore.Notifications;
using MiningCore.Time;

namespace MiningCore.Blockchain.BitcoinGold
{
    public class BitcoinGoldJobManager : ZCashJobManager<BitcoinGoldJob>
    {
        public BitcoinGoldJobManager(IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) : base(ctx, notificationService, clock, extraNonceProvider)
        {
            getBlockTemplateParams = new object[]
            {
                new
                {
                    capabilities = new[] { "coinbasetxn", "workid", "coinbase/append" },
                    rules = new[] { "segwit" }
                }
            };
        }

        #region Overrides of ZCashJobManager<BitcoinGoldJob>

        protected override async Task<DaemonResponse<ZCashBlockTemplate>> GetBlockTemplateAsync()
        {
            var result = await daemon.ExecuteCmdAnyAsync<ZCashBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            return result;
        }

        #endregion
    }
}
