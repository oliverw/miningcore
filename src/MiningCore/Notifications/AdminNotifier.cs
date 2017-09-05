/* 
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial 
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE 
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

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
