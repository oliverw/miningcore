using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MiningCore.Configuration;
using NLog;

namespace MiningCore.Notifications
{
    [NotificationSenderMetadata(NotificationType.Email)]
    public class EmailSender : INotificationSender
    {
        public EmailSender(EmailSenderConfig config)
        {
            this.config = config;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly EmailSenderConfig config;

        #region Implementation of INotificationSender

        public async Task NotifyAsync(string recipient, string subject, string body)
        {
            logger.Info(() => $"Sending '{subject.ToLower()}' email to {recipient}");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
            message.To.Add(new MailboxAddress("", recipient));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(config.Host, config.Port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(config.User, config.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            logger.Info(() => $"Sent '{subject.ToLower()}' email to {recipient}");
        }

        #endregion // INotificationSender
    }
}
