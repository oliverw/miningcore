using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Notifications.Messages
{
    public class AdminNotification
    {
        public AdminNotification(string subject, string message)
        {
            Subject = subject;
            Message = message;
        }

        public string Subject { get; }
        public string Message { get; }
    }
}
