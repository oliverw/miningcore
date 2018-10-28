using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Newtonsoft.Json;
using NLog;

namespace Miningcore.Notifications
{
    public class NotificationService
    {
        public NotificationService(
            ClusterConfig clusterConfig,
            JsonSerializerSettings serializerSettings,
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clusterConfig = clusterConfig;
            this.serializerSettings = serializerSettings;

            poolConfigs = clusterConfig.Pools.ToDictionary(x => x.Id, x => x);

            adminEmail = clusterConfig.Notifications?.Admin?.EmailAddress;
            //adminPhone = null;

            if (clusterConfig.Notifications?.Enabled == true)
            {
                queue = new BlockingCollection<QueuedNotification>();

                queueSub = queue.GetConsumingEnumerable()
                    .ToObservable(TaskPoolScheduler.Default)
                    .Select(notification => Observable.FromAsync(() => SendNotificationAsync(notification)))
                    .Concat()
                    .Subscribe();

                messageBus.Listen<AdminNotification>()
                    .Subscribe(x =>
                    {
                        queue?.Add(new QueuedNotification
                        {
                            Category = NotificationCategory.Admin,
                            Subject = x.Subject,
                            Msg = x.Message
                        });
                    });

                messageBus.Listen<BlockNotification>()
                    .Subscribe(x =>
                    {
                        queue?.Add(new QueuedNotification
                        {
                            Category = NotificationCategory.Block,
                            PoolId = x.PoolId,
                            Subject = "Block Notification",
                            Msg = $"Pool {x.PoolId} found block candidate {x.BlockHeight}"
                        });
                    });

                messageBus.Listen<PaymentNotification>()
                    .Subscribe(x =>
                    {
                        if (string.IsNullOrEmpty(x.Error))
                        {
                            queue?.Add(new QueuedNotification
                            {
                                Category = NotificationCategory.PaymentSuccess,
                                PoolId = x.PoolId,
                                Subject = "Payout Success Notification",
                                Msg = $"Paid {FormatAmount(x.Amount, x.PoolId)} from pool {x.PoolId} to {x.RecpientsCount} recipients in Transaction(s) {x.TxLinks}."
                            });
                        }

                        else
                        {
                            queue?.Add(new QueuedNotification
                            {
                                Category = NotificationCategory.PaymentFailure,
                                PoolId = x.PoolId,
                                Subject = "Payout Failure Notification",
                                Msg = $"Failed to pay out {x.Amount} {poolConfigs[x.PoolId].Template.Symbol} from pool {x.PoolId}: {x.Error}"
                            });
                        }
                    });
            }
        }

        private readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly ClusterConfig clusterConfig;
        private readonly JsonSerializerSettings serializerSettings;
        private readonly Dictionary<string, PoolConfig> poolConfigs;

        private readonly string adminEmail;

        //private readonly string adminPhone;
        private readonly BlockingCollection<QueuedNotification> queue;

        private readonly Regex regexStripHtml = new Regex(@"<[^>]*>", RegexOptions.Compiled);
        private IDisposable queueSub;

        private readonly HttpClient httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
        });

        enum NotificationCategory
        {
            Admin,
            Block,
            PaymentSuccess,
            PaymentFailure,
        }

        struct QueuedNotification
        {
            public NotificationCategory Category;
            public string PoolId;
            public string Subject;
            public string Msg;
        }

        public string FormatAmount(decimal amount, string poolId)
        {
            return $"{amount:0.#####} {poolConfigs[poolId].Template.Symbol}";
        }

        private async Task SendNotificationAsync(QueuedNotification notification)
        {
            logger.Debug(() => $"SendNotificationAsync");

            try
            {
                var poolConfig = !string.IsNullOrEmpty(notification.PoolId) ? poolConfigs[notification.PoolId] : null;

                switch(notification.Category)
                {
                    case NotificationCategory.Admin:
                        if (clusterConfig.Notifications?.Admin?.Enabled == true)
                            await SendEmailAsync(adminEmail, notification.Subject, notification.Msg);
                        break;

                    case NotificationCategory.Block:
                        if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                            clusterConfig.Notifications?.Admin?.NotifyBlockFound == true)
                            await SendEmailAsync(adminEmail, notification.Subject, notification.Msg);
                        break;

                    case NotificationCategory.PaymentSuccess:
                        if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                            clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
                            await SendEmailAsync(adminEmail, notification.Subject, notification.Msg);
                        break;

                    case NotificationCategory.PaymentFailure:
                        if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                            clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
                            await SendEmailAsync(adminEmail, notification.Subject, notification.Msg);
                        break;
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex, $"Error sending notification");
            }
        }

        public async Task SendEmailAsync(string recipient, string subject, string body)
        {
            logger.Info(() => $"Sending '{subject.ToLower()}' email to {recipient}");

            var emailSenderConfig = clusterConfig.Notifications.Email;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSenderConfig.FromName, emailSenderConfig.FromAddress));
            message.To.Add(new MailboxAddress("", recipient));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            using(var client = new SmtpClient())
            {
                await client.ConnectAsync(emailSenderConfig.Host, emailSenderConfig.Port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(emailSenderConfig.User, emailSenderConfig.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            logger.Info(() => $"Sent '{subject.ToLower()}' email to {recipient}");
        }
    }
}
