using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Crypto;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Payments;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using ProtoBuf;
using ZeroMQ;
using ZeroMQ.Monitoring;

namespace Miningcore.Mining
{
    /// <summary>
    /// Receives external shares from relays and re-publishes for consumption
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

        private void StartListeners()
        {
            var serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            var knownPools = new HashSet<string>(clusterConfig.Pools.Select(x => x.Id));

            foreach (var relay in clusterConfig.ShareRelays)
            {
                Task.Run(()=>
                {
                    var url = relay.Url;

                    // Receive loop
                    var done = false;

                    while (!done)
                    {
                        try
                        {
                            using(var subSocket = new ZSocket(ZSocketType.SUB))
                            {
                                subSocket.SetupCurveTlsClient(relay.SharedEncryptionKey, logger);
                                subSocket.Connect(url);
                                subSocket.SubscribeAll();

                                if (subSocket.CurveServerKey != null)
                                    logger.Info($"Monitoring external stratum {url} using Curve public-key {subSocket.CurveServerKey.ToHexString()}");
                                else
                                    logger.Info($"Monitoring external stratum {url}");

                                while (true)
                                {
                                    string topic;
                                    uint flags;
                                    byte[] data;

                                    // receive
                                    using(var msg = subSocket.ReceiveMessage())
                                    {
                                        // extract frames
                                        topic = msg[0].ToString(Encoding.UTF8);
                                        flags = msg[1].ReadUInt32();
                                        data = msg[2].Read();
                                    }

                                    // validate
                                    if (string.IsNullOrEmpty(topic) || !knownPools.Contains(topic))
                                    {
                                        logger.Warn(() => $"Received share for pool '{topic}' which is not known locally. Ignoring ...");
                                        continue;
                                    }

                                    if (data?.Length == 0)
                                    {
                                        logger.Warn(() => $"Received empty data from {url}/{topic}. Ignoring ...");
                                        continue;
                                    }

                                    // TMP FIX
                                    if ((flags & ShareRelay.WireFormatMask) == 0)
                                        flags = BitConverter.ToUInt32(BitConverter.GetBytes(flags).ToNewReverseArray());

                                    // deserialize
                                    var wireFormat = (ShareRelay.WireFormat) (flags & ShareRelay.WireFormatMask);

                                    Share share = null;

                                    switch(wireFormat)
                                    {
                                        case ShareRelay.WireFormat.Json:
                                            using(var stream = new MemoryStream(data))
                                            {
                                                using(var reader = new StreamReader(stream, Encoding.UTF8))
                                                {
                                                    using(var jreader = new JsonTextReader(reader))
                                                    {
                                                        share = serializer.Deserialize<Share>(jreader);
                                                    }
                                                }
                                            }

                                            break;

                                        case ShareRelay.WireFormat.ProtocolBuffers:
                                            using(var stream = new MemoryStream(data))
                                            {
                                                share = Serializer.Deserialize<Share>(stream);
                                                share.BlockReward = (decimal) share.BlockRewardDouble;
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
                                            pool.NetworkStats.BlockHeight = (ulong) share.BlockHeight;
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

                        catch(ObjectDisposedException)
                        {
                            logger.Info($"Exiting monitoring thread for external stratum {url}]");
                            break;
                        }

                        catch(Exception ex)
                        {
                            logger.Error(ex);
                        }
                    }
                });
            }

            if (clusterConfig.ShareRelays.Any())
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

            if(clusterConfig.ShareRelays != null)
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
