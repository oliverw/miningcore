using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using NLog;
using ProtoBuf;

namespace MiningCore.Mining
{
    public class ShareRelay
    {
        public ShareRelay(JsonSerializerSettings serializerSettings)
        {
            this.serializerSettings = serializerSettings;
        }

        private ClusterConfig clusterConfig;
        private readonly BlockingCollection<Share> queue = new BlockingCollection<Share>();
        private IDisposable queueSub;
        private readonly int QueueSizeWarningThreshold = 1024;
        private bool hasWarnedAboutBacklogSize;
        private PublisherSocket pubSocket;
        private readonly JsonSerializerSettings serializerSettings;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        [Flags]
        public enum WireFormat
        {
            Json = 1,
            ProtocolBuffers = 2
        }

        public const int WireFormatMask = 0xF;

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
                    share.BlockRewardDouble = (double) share.BlockReward;

                    try
                    {
                        var flags = (int) WireFormat.ProtocolBuffers;
                        var msg = new NetMQMessage(2);

                        using (var stream = new MemoryStream())
                        {
                            Serializer.Serialize(stream, share);
                            msg.Push(stream.ToArray());
                        }

                        msg.Push(flags);
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
