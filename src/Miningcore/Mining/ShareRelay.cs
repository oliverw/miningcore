using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using ProtoBuf;
using ZeroMQ;

namespace Miningcore.Mining
{
    public class ShareRelay : IHostedService
    {
        public ShareRelay(
            ClusterConfig clusterConfig,
            JsonSerializerSettings serializerSettings,
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clusterConfig = clusterConfig;
            this.serializerSettings = serializerSettings;
            this.messageBus = messageBus;
        }

        private readonly IMessageBus messageBus;
        private readonly ClusterConfig clusterConfig;
        private readonly BlockingCollection<Share> queue = new();
        private IDisposable queueSub;
        private readonly int QueueSizeWarningThreshold = 1024;
        private bool hasWarnedAboutBacklogSize;
        private ZSocket pubSocket;
        private readonly JsonSerializerSettings serializerSettings;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        [Flags]
        public enum WireFormat
        {
            Json = 1,
            ProtocolBuffers = 2
        }

        public const int WireFormatMask = 0xF;

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

                        using(var msg = new ZMessage())
                        {
                            // Topic frame
                            msg.Add(new ZFrame(share.PoolId));

                            // Frame 2: flags
                            msg.Add(new ZFrame(flags));

                            // Frame 3: payload
                            using(var stream = new MemoryStream())
                            {
                                Serializer.Serialize(stream, share);
                                msg.Add(new ZFrame(stream.ToArray()));
                            }

                            pubSocket.SendMessage(msg);
                        }
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }
                });
        }

        private void CheckQueueBacklog()
        {
            if(queue.Count > QueueSizeWarningThreshold)
            {
                if(!hasWarnedAboutBacklogSize)
                {
                    logger.Warn(() => $"Share relay queue backlog has crossed {QueueSizeWarningThreshold}");
                    hasWarnedAboutBacklogSize = true;
                }
            }

            else if(hasWarnedAboutBacklogSize && queue.Count <= QueueSizeWarningThreshold / 2)
            {
                hasWarnedAboutBacklogSize = false;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            messageBus.Listen<ClientShare>().Subscribe(x => queue.Add(x.Share));

            pubSocket = new ZSocket(ZSocketType.PUB);

            if(!clusterConfig.ShareRelay.Connect)
            {
                pubSocket.SetupCurveTlsServer(clusterConfig.ShareRelay.SharedEncryptionKey, logger);

                pubSocket.Bind(clusterConfig.ShareRelay.PublishUrl);

                if(pubSocket.CurveServer)
                    logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl} using Curve public-key {pubSocket.CurvePublicKey.ToHexString()}");
                else
                    logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl}");
            }

            else
            {
                if(!string.IsNullOrEmpty(clusterConfig.ShareRelay.SharedEncryptionKey?.Trim()))
                    logger.ThrowLogPoolStartupException("ZeroMQ Curve is not supported in ShareRelay Connect-Mode");

                pubSocket.Connect(clusterConfig.ShareRelay.PublishUrl);
                logger.Info(() => $"Connected to {clusterConfig.ShareRelay.PublishUrl}");
            }

            InitializeQueue();

            logger.Info(() => "Online");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            pubSocket.Dispose();

            queueSub?.Dispose();
            queueSub = null;

            return Task.CompletedTask;
        }
    }
}
