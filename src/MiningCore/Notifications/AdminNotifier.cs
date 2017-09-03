using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac.Features.Metadata;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Mining;
using NLog;

namespace MiningCore.Notifications
{
    public class AdminNotifier
    {
        public AdminNotifier(IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders)
        {
            this.notificationSenders = notificationSenders;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders;
        private AdminNotifications config;
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        #region API Surface

        public void Configure(AdminNotifications config)
        {
            this.config = config;
        }

        public void AttachPool(IMiningPool pool)
        {
            if (config.NotifyBlockFound)
            {
                disposables.Add(pool.Shares
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Where(x=> x.IsBlockCandidate)
                    .Subscribe(OnBlockFound));
            }
        }

        #endregion // API Surface

        private async void OnBlockFound(IShare share)
        {
            var emailSender = notificationSenders
                .Where(x => x.Metadata.NotificationType == NotificationType.Email)
                .Select(x => x.Value)
                .First();

            try
            {
                await emailSender.NotifyAsync(config.EmailAddress, "Block Notification", $"Pool {share.PoolId} found block candidate {share.BlockHeight}");
            }

            catch (Exception ex)
            {
                logger.Error(ex, ()=> $"{nameof(AdminNotifier)}");
            }
        }
    }
}
