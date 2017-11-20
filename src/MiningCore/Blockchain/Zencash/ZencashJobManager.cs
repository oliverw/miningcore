using System.Threading.Tasks;
using Autofac;
using MiningCore.Blockchain.Bitcoin;
using MiningCore.Blockchain.ZCash;
using MiningCore.Blockchain.ZCash.DaemonResponses;
using MiningCore.Blockchain.Zencash;
using MiningCore.DaemonInterface;
using MiningCore.Notifications;
using MiningCore.Time;

namespace MiningCore.Blockchain.Zencash
{
    public class ZencashJobManager : ZCashJobManager<ZencashJob>
    {
        public ZencashJobManager(IComponentContext ctx,
            NotificationService notificationService,
            IMasterClock clock,
            IExtraNonceProvider extraNonceProvider) : base(ctx, notificationService, clock, extraNonceProvider)
        {
        }

        #region Overrides of ZCashJobManager<ZencashJob>

        protected override async Task<DaemonResponse<ZCashBlockTemplate>> GetBlockTemplateAsync()
        {
            var result = await daemon.ExecuteCmdAnyAsync<ZCashBlockTemplate>(
                BitcoinCommands.GetBlockTemplate, getBlockTemplateParams);

            return result;
        }

        #endregion
    }
}
