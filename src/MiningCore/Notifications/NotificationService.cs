using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Autofac.Features.Metadata;
using MiningCore.Configuration;
using MiningCore.Contracts;
using NLog;

namespace MiningCore.Notifications
{
    public class NotificationService
    {
        public NotificationService(
            ClusterConfig clusterConfig,
            IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders)
        {
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(notificationSenders, nameof(notificationSenders));

            this.clusterConfig = clusterConfig;
            this.notificationSenders = notificationSenders;

            adminEmail = clusterConfig.Notifications?.Admin?.EmailAddress;
            adminPhone = null;

            queueSub = queue.GetConsumingEnumerable()
                .ToObservable(TaskPoolScheduler.Default)
                .Select(notification => Observable.FromAsync(() => SendNotificationAsync(notification)))
                .Concat()
                .Subscribe();
        }

        private readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IEnumerable<Meta<INotificationSender, NotificationSenderMetadataAttribute>> notificationSenders;
        private readonly ClusterConfig clusterConfig;
        private readonly string adminEmail;
        private readonly string adminPhone;
        private readonly BlockingCollection<QueuedNotification> queue = new BlockingCollection<QueuedNotification>();
        private IDisposable queueSub;

        enum NotificationCategory
        {
            Admin,
            Miner,
        }

        class QueuedNotification
        {
            public NotificationCategory Category;
            public string Subject;
            public string Msg;
            public string Recipient;
        }

        #region API-Surface

        public void NotifyAdmin(string subject, string msg)
        {
            queue.Add(new QueuedNotification
            {
                Category = NotificationCategory.Admin,
                Subject = subject,
                Msg = msg
            });
        }

        public void NotifyMiner(string subject, string msg, string recipient)
        {
            queue.Add(new QueuedNotification
            {
                Category = NotificationCategory.Miner,
                Subject = subject,
                Msg = msg,
                Recipient = recipient,
            });
        }

        #endregion // API-Surface

        private async Task SendNotificationAsync(QueuedNotification notification)
        {
            foreach(var sender in notificationSenders)
            {
                try
                {
                    string recipient = null;

                    // assign recipient if necessary
                    if (notification.Category != NotificationCategory.Admin)
                        recipient = notification.Recipient;
                    else
                    {
                        switch(sender.Metadata.NotificationType)
                        {
                            case NotificationType.Email:
                                recipient = adminEmail;
                                break;

                            case NotificationType.Sms:
                                recipient = adminPhone;
                                break;
                        }
                    }

                    if (string.IsNullOrEmpty(recipient))
                    {
                        logger.Warn(() => $"No recipient for {notification.Category.ToString().ToLower()}");
                        continue;
                    }

                    // send it
                    await sender.Value.NotifyAsync(recipient, notification.Subject, notification.Msg);
                }

                catch(Exception ex)
                {
                    logger.Error(ex, $"Error sending notification using {sender.GetType().Name}");
                }
            }
        }
    }
}
