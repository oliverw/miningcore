using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using NLog;
using static Miningcore.Util.ActionUtils;

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
            await SendEmailAsync(adminEmail, notification.Subject, notification.Message, ct);
        }

        private async Task OnBlockFoundNotificationAsync(BlockFoundNotification notification, CancellationToken ct)
        {
            await SendEmailAsync(adminEmail, "Block Notification", $"Pool {notification.PoolId} found block candidate {notification.BlockHeight}", ct);
        }

        private async Task OnPaymentNotificationAsync(PaymentNotification notification, CancellationToken ct)
        {
            if(string.IsNullOrEmpty(notification.Error))
            {
                var coin = poolConfigs[notification.PoolId].Template;

                // prepare tx links
                string[] txLinks = null;

                if(!string.IsNullOrEmpty(coin.ExplorerTxLink))
                    txLinks = notification.TxIds.Select(txHash => string.Format(coin.ExplorerTxLink, txHash)).ToArray();

                if(clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
                    await SendEmailAsync(adminEmail, "Payout Success Notification", $"Paid {FormatAmount(notification.Amount, notification.PoolId)} from pool {notification.PoolId} to {notification.RecpientsCount} recipients in Transaction(s) {txLinks}.", ct);
            }

            else
            {
                await SendEmailAsync(adminEmail, "Payout Failure Notification",
                    $"Failed to pay out {notification.Amount} {poolConfigs[notification.PoolId].Template.Symbol} from pool {notification.PoolId}: {notification.Error}", ct);
            }
        }

        public async Task SendEmailAsync(string recipient, string subject, string body, CancellationToken ct)
        {
            logger.Info(() => $"Sending '{subject.ToLower()}' email to {recipient}");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(emailSenderConfig.FromName, emailSenderConfig.FromAddress));
            message.To.Add(new MailboxAddress("", recipient));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            using(var client = new SmtpClient())
            {
                await client.ConnectAsync(emailSenderConfig.Host, emailSenderConfig.Port, cancellationToken: ct);
                await client.AuthenticateAsync(emailSenderConfig.User, emailSenderConfig.Password, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
            }

            logger.Info(() => $"Sent '{subject.ToLower()}' email to {recipient}");
        }

        private IObservable<IObservable<Unit>> OnMessageBus<T>(Func<T, CancellationToken, Task> handler, CancellationToken ct)
        {
            return messageBus
                .Listen<T>()
                .Select(msg => Observable.FromAsync(() =>
                    Guard(()=> handler(msg, ct), ex=> logger.Error(ex))));
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var obs = new List<IObservable<IObservable<Unit>>>();

            if(clusterConfig.Notifications?.Admin?.Enabled == true)
            {
                obs.Add(OnMessageBus<AdminNotification>(OnAdminNotificationAsync, ct));
                obs.Add(OnMessageBus<BlockFoundNotification>(OnBlockFoundNotificationAsync, ct));
                obs.Add(OnMessageBus<PaymentNotification>(OnPaymentNotificationAsync, ct));
            }

            if(obs.Count > 0)
            {
                await obs
                    .Merge()
                    .ObserveOn(TaskPoolScheduler.Default)
                    .Concat()
                    .ToTask(ct);
            }
        }
    }
}
