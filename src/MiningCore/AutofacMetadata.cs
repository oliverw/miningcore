using System;
using System.Collections.Generic;
using MiningCore.Configuration;
using MiningCore.Notifications;

namespace MiningCore
{
    public class CoinMetadataAttribute : Attribute
    {
        public CoinMetadataAttribute(IDictionary<string, object> values)
        {
            if (values.ContainsKey(nameof(SupportedCoins)))
                SupportedCoins = (CoinType[]) values[nameof(SupportedCoins)];
        }

        public CoinMetadataAttribute(params CoinType[] supportedCoins)
        {
            SupportedCoins = supportedCoins;
        }

        public CoinType[] SupportedCoins { get; }
    }

    public class NotificationSenderMetadataAttribute : Attribute
    {
        public NotificationSenderMetadataAttribute(IDictionary<string, object> values)
        {
            if(values.ContainsKey(nameof(NotificationType)))
                NotificationType = (NotificationType) values[nameof(NotificationType)];
        }

        public NotificationSenderMetadataAttribute(NotificationType notificationType)
        {
            NotificationType = notificationType;
        }

        public NotificationType NotificationType { get; }
    }
}
