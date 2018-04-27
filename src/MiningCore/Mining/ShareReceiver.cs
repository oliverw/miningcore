using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Contracts;
using MiningCore.Messaging;
using MiningCore.Time;
using MiningCore.Util;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using ProtoBuf;

namespace MiningCore.Mining
{
    /// <summary>
    /// Received external shares from relay and re-publishes for consumption
    /// </summary>
    public class ShareReceiver
    {
        public ShareReceiver(IMasterClock clock, IMessageBus messageBus)
        {
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.clock = clock;
            this.messageBus = messageBus;
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IMasterClock clock;
        private readonly IMessageBus messageBus;
        private ClusterConfig clusterConfig;
        private readonly ConcurrentDictionary<string, PoolContext> pools = new ConcurrentDictionary<string, PoolContext>();

        class PoolContext
        {
            public PoolContext(IMiningPool pool, ILogger logger)
            {
                Pool = pool;
                Logger = logger;
            }

            public readonly IMiningPool Pool;
            public readonly ILogger Logger;
            public DateTime? LastBlock;
            public long BlockHeight;
        }

        private readonly TimeSpan relayReceiveTimeout = TimeSpan.FromSeconds(60);

        private void StartListeners()
        {
            var stratumsByUrl = clusterConfig.Pools.Where(x => x.ExternalStratums?.Any() == true)
                .SelectMany(x => x.ExternalStratums)
                .Where(x => x.Url != null && x.Topic != null)
                .GroupBy(x =>
                {
                    var tmp = x.Url.Trim();
                    return !tmp.EndsWith("/") ? tmp : tmp.Substring(0, tmp.Length - 1);
                }, x => x.Topic.Trim())
                .ToArray();

            var serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            foreach (var item in stratumsByUrl)
            {
                var thread = new Thread(arg =>
                {
                    var urlAndTopic = (IGrouping<string, string>)arg;
                    var url = urlAndTopic.Key;
                    var topics = new HashSet<string>(urlAndTopic.Distinct());
                    var receivedOnce = false;

                    while (true)
                    {
                        try
                        {
                            using (var subSocket = new SubscriberSocket())
                            {
                                subSocket.Connect(url);

                                // subscribe to all topics
                                foreach (var topic in topics)
                                    subSocket.Subscribe(topic);

                                logger.Info($"Monitoring external stratum {url}/[{string.Join(", ", topics)}]");

                                while (true)
                                {
                                    // receive
                                    var msg = (NetMQMessage)null;

                                    if (!subSocket.TryReceiveMultipartMessage(relayReceiveTimeout, ref msg, 3))
                                    {
                                        if (receivedOnce)
                                        {
                                            logger.Warn(() => $"Timeout receiving message from {url}. Reconnecting ...");
                                            break;
                                        }

                                        // retry
                                        continue;
                                    }

                                    // extract frames
                                    var topic = msg.Pop().ConvertToString(Encoding.UTF8);
                                    var flags = msg.Pop().ConvertToInt32();
                                    var data = msg.Pop().ToByteArray();
                                    receivedOnce = true;

                                    // validate
                                    if (!topics.Contains(topic))
                                    {
                                        logger.Warn(() => $"Received non-matching topic {topic} on ZeroMQ subscriber socket");
                                        continue;
                                    }

                                    if (data?.Length == 0)
                                    {
                                        logger.Warn(() => $"Received empty data from {url}/{topic}");
                                        continue;
                                    }

                                    // deserialize
                                    var wireFormat = (ShareRelay.WireFormat)(flags & ShareRelay.WireFormatMask);
                                    Share share = null;

                                    switch (wireFormat)
                                    {
                                        case ShareRelay.WireFormat.Json:
                                            using (var stream = new MemoryStream(data))
                                            {
                                                using (var reader = new StreamReader(stream, Encoding.UTF8))
                                                {
                                                    using (var jreader = new JsonTextReader(reader))
                                                    {
                                                        share = serializer.Deserialize<Share>(jreader);
                                                    }
                                                }
                                            }
                                            break;

                                        case ShareRelay.WireFormat.ProtocolBuffers:
                                            using (var stream = new MemoryStream(data))
                                            {
                                                share = Serializer.Deserialize<Share>(stream);
                                                share.BlockReward = (decimal)share.BlockRewardDouble;
                                            }
                                            break;

                                        default:
                                            logger.Error(() => $"Unsupported wire format {wireFormat} of share received from {url}/{topic} ");
                                            break;
                                    }

                                    if (share == null)
                                    {
                                        logger.Error(() => $"Unable to deserialize share received from {url}/{topic}");
                                        continue;
                                    }

                                    // store
                                    share.PoolId = topic;
                                    share.Created = clock.Now;
                                    messageBus.SendMessage(new ClientShare(null, share));

                                    // update poolstats from shares
                                    if (pools.TryGetValue(topic, out var poolContext))
                                    {
                                        var pool = poolContext.Pool;
                                        poolContext.Logger.Info(() => $"External {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}share accepted: D={Math.Round(share.Difficulty, 3)}");

                                        if (pool.NetworkStats != null)
                                        {
                                            pool.NetworkStats.BlockHeight = share.BlockHeight;
                                            pool.NetworkStats.NetworkDifficulty = share.NetworkDifficulty;

                                            if (poolContext.BlockHeight != share.BlockHeight)
                                            {
                                                pool.NetworkStats.LastNetworkBlockTime = clock.Now;
                                                poolContext.BlockHeight = share.BlockHeight;
                                                poolContext.LastBlock = clock.Now;
                                            }

                                            else
                                                pool.NetworkStats.LastNetworkBlockTime = poolContext.LastBlock;
                                        }
                                    }

                                    else
                                        logger.Info(() => $"External {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}share accepted: D={Math.Round(share.Difficulty, 3)}");
                                }
                            }
                        }

                        catch (ObjectDisposedException)
                        {
                            logger.Info($"Exiting monitoring thread for external stratum {url}/[{string.Join(", ", topics)}]");
                            break;
                        }

                        catch (Exception ex)
                        {
                            logger.Error(ex);
                        }
                    }
                });

                thread.Start(item);
            }

            if(stratumsByUrl.Any())
                logger.Info(() => "Online");
        }

        #region API-Surface

        public void AttachPool(IMiningPool pool)
        {
            pools[pool.Config.Id] = new PoolContext(pool, LogUtil.GetPoolScopedLogger(typeof(ShareRecorder), pool.Config));
        }

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            StartListeners();
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface
    }
}
