using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Mining;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using NLog;

namespace MiningCore.Payments
{
    public class ShareRelay
    {
        public ShareRelay(JsonSerializerSettings serializerSettings)
        {
            this.serializerSettings = serializerSettings;
        }

        private ClusterConfig clusterConfig;
        private readonly BlockingCollection<IShare> queue = new BlockingCollection<IShare>();
        private IDisposable queueSub;
        private readonly int QueueSizeWarningThreshold = 1024;
        private bool hasWarnedAboutBacklogSize;
        private PublisherSocket pubSocket;
        private readonly JsonSerializerSettings serializerSettings;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        #region API-Surface

        public void AttachPool(IMiningPool pool)
        {
            pool.Shares.Subscribe(x => { queue.Add(x.Share); });
        }

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            pubSocket = new PublisherSocket();

            if (!clusterConfig.ShareRelay.Connect)
            {
                pubSocket.Bind(clusterConfig.ShareRelay.PublishUrl);
                logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl}");
            }

            else
            {
                pubSocket.Connect(clusterConfig.ShareRelay.PublishUrl);
                logger.Info(() => $"Connected to {clusterConfig.ShareRelay.PublishUrl}");
            }

            InitializeQueue();

            logger.Info(() => "Online");
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            pubSocket.Dispose();

            queueSub?.Dispose();
            queueSub = null;

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface

        private void InitializeQueue()
        {
            queueSub = queue.GetConsumingEnumerable()
                .ToObservable(TaskPoolScheduler.Default)
                .Do(_ => CheckQueueBacklog())
                .Subscribe(share =>
                {
                    share.Source = clusterConfig.ClusterName;

                    try
                    {
                        var json = JsonConvert.SerializeObject(share, serializerSettings);

                        var msg = new NetMQMessage(2);
                        msg.Push(json);
                        msg.Push(share.PoolId);
                        pubSocket.SendMultipartMessage(msg);
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                });
        }

        private void CheckQueueBacklog()
        {
            if (queue.Count > QueueSizeWarningThreshold)
            {
                if (!hasWarnedAboutBacklogSize)
                {
                    logger.Warn(() => $"Share relay queue backlog has crossed {QueueSizeWarningThreshold}");
                    hasWarnedAboutBacklogSize = true;
                }
            }

            else if (hasWarnedAboutBacklogSize && queue.Count <= QueueSizeWarningThreshold / 2)
            {
                hasWarnedAboutBacklogSize = false;
            }
        }
    }
}
