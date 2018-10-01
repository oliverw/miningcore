using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Crypto;
using MiningCore.Extensions;
using MiningCore.Messaging;
using MiningCore.Util;
using Newtonsoft.Json;
using NLog;
using ProtoBuf;
using ZeroMQ;

namespace MiningCore.Mining
{
    public class ShareRelay
    {
        public ShareRelay(JsonSerializerSettings serializerSettings, IMessageBus messageBus)
        {
            Contract.RequiresNonNull(serializerSettings, nameof(serializerSettings));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.serializerSettings = serializerSettings;
            this.messageBus = messageBus;
        }

        private readonly IMessageBus messageBus;
        private ClusterConfig clusterConfig;
        private readonly BlockingCollection<Share> queue = new BlockingCollection<Share>();
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

        #region API-Surface

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            messageBus.Listen<ClientShare>().Subscribe(x => queue.Add(x.Share));

            pubSocket = new ZSocket(ZSocketType.PUB);

            // ZMQ Curve Transport-Layer-Security
            var keyPlain = clusterConfig.ShareRelay.SharedEncryptionKey?.Trim();

            if (!string.IsNullOrEmpty(keyPlain))
            {
                if(!ZContext.Has("curve"))
                    logger.ThrowLogPoolStartupException("Unable to initialize ZMQ Curve Transport-Layer-Security. Your ZMQ library was compiled without Curve support!");

                var keyBytes = KeyFactory.Derive256BitKey(keyPlain);
                Z85.CurvePublic(out var publicKey, keyBytes.ToZ85Encoded());

                pubSocket.CurveServer = true;
                pubSocket.CurveSecretKey = keyBytes;
                pubSocket.CurvePublicKey = publicKey;
            }

            if (!clusterConfig.ShareRelay.Connect)
            {
                pubSocket.Bind(clusterConfig.ShareRelay.PublishUrl);

                if(pubSocket.CurveServer)
                    logger.Info(() => $"Bound to {clusterConfig.ShareRelay.PublishUrl} using Curve public-key {pubSocket.CurvePublicKey.ToHexString()}");
                else
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

                        using (var msg = new ZMessage())
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
