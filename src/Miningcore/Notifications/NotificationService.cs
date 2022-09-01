using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Pushover;
using NLog;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Notifications;

public class NotificationService : BackgroundService
{
    public NotificationService(
        ClusterConfig clusterConfig,
        PushoverClient pushoverClient,
        IMessageBus messageBus)
    {
        Contract.RequiresNonNull(clusterConfig);
        Contract.RequiresNonNull(messageBus);

        this.clusterConfig = clusterConfig;
        emailSenderConfig = clusterConfig.Notifications.Email;
        this.messageBus = messageBus;
        this.pushoverClient = pushoverClient;

        poolConfigs = clusterConfig.Pools.ToDictionary(x => x.Id, x => x);

        adminEmail = clusterConfig.Notifications?.Admin?.EmailAddress;
    }

    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly ClusterConfig clusterConfig;
    private readonly Dictionary<string, PoolConfig> poolConfigs;
    private readonly string adminEmail;
    private readonly IMessageBus messageBus;
    private readonly EmailSenderConfig emailSenderConfig;
    private readonly PushoverClient pushoverClient;

    public string FormatAmount(decimal amount, string poolId)
    {
        return $"{amount:0.#####} {poolConfigs[poolId].Template.Symbol}";
    }

    private async Task OnAdminNotificationAsync(AdminNotification notification, CancellationToken ct)
    {
        if(!string.IsNullOrEmpty(adminEmail))
            await Guard(()=> SendEmailAsync(adminEmail, notification.Subject, notification.Message, ct), LogGuarded);

        if(clusterConfig.Notifications?.Pushover?.Enabled == true)
            await Guard(()=> pushoverClient.PushMessage(notification.Subject, notification.Message, PushoverMessagePriority.None, ct), LogGuarded);
    }

    private async Task OnBlockFoundNotificationAsync(BlockFoundNotification notification, CancellationToken ct)
    {
        const string subject = "Block Notification";
        var message = $"Pool {notification.PoolId} found block candidate {notification.BlockHeight}";

        if(clusterConfig.Notifications?.Admin?.NotifyBlockFound == true)
        {
            await Guard(() => SendEmailAsync(adminEmail, subject, message, ct), LogGuarded);

            if(clusterConfig.Notifications?.Pushover?.Enabled == true)
                await Guard(() => pushoverClient.PushMessage(subject, message, PushoverMessagePriority.None, ct), LogGuarded);
        }
    }

    private async Task OnPaymentNotificationAsync(PaymentNotification notification, CancellationToken ct)
    {
        if(string.IsNullOrEmpty(notification.Error))
        {
            var coin = poolConfigs[notification.PoolId].Template;

            // prepare tx links
            var txLinks = Array.Empty<string>();

            if(!string.IsNullOrEmpty(coin.ExplorerTxLink))
                txLinks = notification.TxIds.Select(txHash => string.Format(coin.ExplorerTxLink, txHash)).ToArray();

            const string subject = "Payout Success Notification";
            var message = $"Paid {FormatAmount(notification.Amount, notification.PoolId)} from pool {notification.PoolId} to {notification.RecipientsCount} recipients in transaction(s) {(string.Join(", ", txLinks))}";

            if(clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true)
            {
                await Guard(() => SendEmailAsync(adminEmail, subject, message, ct), LogGuarded);

                if(clusterConfig.Notifications?.Pushover?.Enabled == true)
                    await Guard(() => pushoverClient.PushMessage(subject, message, PushoverMessagePriority.None, ct), LogGuarded);
            }
        }

        else
        {
            const string subject = "Payout Failure Notification";
            var message = $"Failed to pay out {notification.Amount} {poolConfigs[notification.PoolId].Template.Symbol} from pool {notification.PoolId}: {notification.Error}";

            await Guard(()=> SendEmailAsync(adminEmail, subject, message, ct), LogGuarded);

            if(clusterConfig.Notifications?.Pushover?.Enabled == true)
                await Guard(()=> pushoverClient.PushMessage(subject, message, PushoverMessagePriority.None, ct), LogGuarded);
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

    private void LogGuarded(Exception ex)
    {
        logger.Error(ex);
    }

    private IObservable<IObservable<Unit>> Subscribe<T>(Func<T, CancellationToken, Task> handler, CancellationToken ct)
    {
        return messageBus.Listen<T>()
            .Select(msg => Observable.FromAsync(() =>
                Guard(()=> handler(msg, ct), LogGuarded)));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var obs = new List<IObservable<IObservable<Unit>>>();

        if(clusterConfig.Notifications?.Admin?.Enabled == true)
        {
            obs.Add(Subscribe<AdminNotification>(OnAdminNotificationAsync, ct));
            obs.Add(Subscribe<BlockFoundNotification>(OnBlockFoundNotificationAsync, ct));
            obs.Add(Subscribe<PaymentNotification>(OnPaymentNotificationAsync, ct));
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
