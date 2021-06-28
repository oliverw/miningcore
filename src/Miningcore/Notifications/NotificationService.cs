using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Util;
using NLog;

namespace Miningcore.Notifications
{
    public class NotificationService : BackgroundService
    {
        public NotificationService(
            ClusterConfig clusterConfig,
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(clusterConfig, nameof(clusterConfig));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clusterConfig = clusterConfig;
            this.emailSenderConfig = clusterConfig.Notifications.Email;
            this.messageBus = messageBus;

            poolConfigs = clusterConfig.Pools.ToDictionary(x => x.Id, x => x);

            adminEmail = clusterConfig.Notifications?.Admin?.EmailAddress;
        }

        private readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly ClusterConfig clusterConfig;
        private readonly Dictionary<string, PoolConfig> poolConfigs;
        private readonly string adminEmail;
        private readonly IMessageBus messageBus;
        private readonly EmailSenderConfig emailSenderConfig;

        public string FormatAmount(decimal amount, string poolId)
        {
            return $"{amount:0.#####} {poolConfigs[poolId].Template.Symbol}";
        }

        private async Task OnAdminNotificationAsync(AdminNotification notification, CancellationToken ct)
        {
            try
            {
                await SendEmailAsync(adminEmail, notification.Subject, notification.Message);
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }

        private async Task OnBlockFoundNotificationAsync(BlockFoundNotification notification, CancellationToken ct)
        {
            try
            {
                await SendEmailAsync(adminEmail, "Block Notification", $"Pool {notification.PoolId} found block candidate {notification.BlockHeight}");
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }

        private async Task OnPaymentNotificationAsync(PaymentNotification notification, CancellationToken ct)
        {
            try
            {
                if(string.IsNullOrEmpty(notification.Error))
                {
                    var coin = poolConfigs[notification.PoolId].Template;

                    // prepare tx links
                    string[] txLinks = null;

                    if(!string.IsNullOrEmpty(coin.ExplorerTxLink))
                        txLinks = notification.TxIds.Select(txHash => string.Format(coin.ExplorerTxLink, txHash)).ToArray();

                    if(clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
                        await SendEmailAsync(adminEmail, "Payout Success Notification", $"Paid {FormatAmount(notification.Amount, notification.PoolId)} from pool {notification.PoolId} to {notification.RecpientsCount} recipients in Transaction(s) {txLinks}.");
                }

                else
                {
                    await SendEmailAsync(adminEmail, "Payout Failure Notification",
                        $"Failed to pay out {notification.Amount} {poolConfigs[notification.PoolId].Template.Symbol} from pool {notification.PoolId}: {notification.Error}");
                }
            }

            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }

        public async Task SendEmailAsync(string recipient, string subject, string body)
        {
            logger.Info(() => $"Sending '{subject.ToLower()}' email to {recipient}");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSenderConfig.FromName, emailSenderConfig.FromAddress));
            message.To.Add(new MailboxAddress("", recipient));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            using(var client = new SmtpClient())
            {
                await client.ConnectAsync(emailSenderConfig.Host, emailSenderConfig.Port);
                await client.AuthenticateAsync(emailSenderConfig.User, emailSenderConfig.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            logger.Info(() => $"Sent '{subject.ToLower()}' email to {recipient}");
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var obs = new List<IObservable<IObservable<Unit>>>
            {
            };

            if(clusterConfig.Notifications?.Admin?.Enabled == true)
            {
                obs.Add(messageBus.Listen<AdminNotification>().Select(x => Observable.FromAsync(() => OnAdminNotificationAsync(x, ct))));
                obs.Add(messageBus.Listen<BlockFoundNotification>().Select(x => Observable.FromAsync(() => OnBlockFoundNotificationAsync(x, ct))));
                obs.Add(messageBus.Listen<PaymentNotification>().Select(x => Observable.FromAsync(() => OnPaymentNotificationAsync(x, ct))));
            }

            if(obs.Count > 0)
                await obs.Merge().ObserveOn(TaskPoolScheduler.Default).Concat().ToTask(ct);
        }
    }
}
