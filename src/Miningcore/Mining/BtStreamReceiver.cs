using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Cryptonote.Configuration;
using Miningcore.Blockchain.Ethereum.Configuration;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Time;
using MoreLinq;
using NLog;
using ZeroMQ;

namespace Miningcore.Mining
{
    /// <summary>
    /// Receives external shares from relays and re-publishes for consumption
    /// </summary>
    public class BtStreamReceiver
    {
        public BtStreamReceiver(IMasterClock clock, IMessageBus messageBus)
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
        private CompositeDisposable disposables = new CompositeDisposable();
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private void StartMessageReceiver(ZmqPubSubEndpointConfig[] endpoints)
        {
            Task.Run(() =>
            {
                Thread.CurrentThread.Name = "BtStreamReceiver Socket Poller";
                var timeout = TimeSpan.FromMilliseconds(1000);
                var reconnectTimeout = TimeSpan.FromSeconds(300);

                var relays = endpoints
                    .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                    .ToArray();

                while(!cts.IsCancellationRequested)
                {
                    // track last message received per endpoint
                    var lastMessageReceived = relays.Select(_ => clock.Now).ToArray();

                    try
                    {
                        // setup sockets
                        var sockets = relays.Select(SetupSubSocket).ToArray();

                        using(new CompositeDisposable(sockets))
                        {
                            var pollItems = sockets.Select(_ => ZPollItem.CreateReceiver()).ToArray();

                            while(!cts.IsCancellationRequested)
                            {
                                if(sockets.PollIn(pollItems, out var messages, out var error, timeout))
                                {
                                    for(var i = 0; i < messages.Length; i++)
                                    {
                                        var msg = messages[i];

                                        if(msg != null)
                                        {
                                            lastMessageReceived[i] = clock.Now;

                                            using(msg)
                                            {
                                                ProcessMessage(msg);
                                            }
                                        }

                                        else if(clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                        {
                                            // re-create socket
                                            sockets[i].Dispose();
                                            sockets[i] = SetupSubSocket(relays[i]);

                                            // reset clock
                                            lastMessageReceived[i] = clock.Now;

                                            logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                        }
                                    }

                                    if(error != null)
                                        logger.Error(() => $"{nameof(ShareReceiver)}: {error.Name} [{error.Name}] during receive");
                                }
                            }
                        }
                    }

                    catch(Exception ex)
                    {
                        logger.Error(() => $"{nameof(ShareReceiver)}: {ex}");

                        if(!cts.IsCancellationRequested)
                            Thread.Sleep(1000);
                    }
                }
            }, cts.Token);
        }

        private static ZSocket SetupSubSocket(ZmqPubSubEndpointConfig relay)
        {
            var subSocket = new ZSocket(ZSocketType.SUB);
            subSocket.SetupCurveTlsClient(relay.SharedEncryptionKey, logger);
            subSocket.Connect(relay.Url);
            subSocket.SubscribeAll();

            if(subSocket.CurveServerKey != null)
                logger.Info($"Monitoring Bt-Stream source {relay.Url} using Curve public-key {subSocket.CurveServerKey.ToHexString()}");
            else
                logger.Info($"Monitoring Bt-Stream source {relay.Url}");

            return subSocket;
        }

        private void ProcessMessage(ZMessage msg)
        {
            // extract frames
            var topic = msg[0].ToString(Encoding.UTF8);
            var flags = msg[1].ReadUInt32();
            var data = msg[2].Read();
            var sent = DateTimeOffset.FromUnixTimeMilliseconds(msg[3].ReadInt64()).DateTime;

            // TMP FIX
            if(flags != 0 && ((flags & 1) == 0))
                flags = BitConverter.ToUInt32(BitConverter.GetBytes(flags).ToNewReverseArray());

            // compressed
            if((flags & 1) == 1)
            {
                using(var stm = new MemoryStream(data))
                {
                    using(var stmOut = new MemoryStream())
                    {
                        using(var ds = new DeflateStream(stm, CompressionMode.Decompress))
                        {
                            ds.CopyTo(stmOut);
                        }

                        data = stmOut.ToArray();
                    }
                }
            }

            // convert
            var content = Encoding.UTF8.GetString(data);

            // publish
            messageBus.SendMessage(new BtStreamMessage(topic, content, sent, DateTime.UtcNow));
        }

        #region API-Surface

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            var endpoints = clusterConfig.Pools.Select(x =>
                    x.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>()?.BtStream ??
                    x.Extra.SafeExtensionDataAs<CryptonotePoolConfigExtra>()?.BtStream ??
                    x.Extra.SafeExtensionDataAs<EthereumPoolConfigExtra>()?.BtStream)
                .Where(x => x != null)
                .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                .ToArray();

            if(endpoints.Any())
                StartMessageReceiver(endpoints);
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            cts.Cancel();
            disposables.Dispose();

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface
    }
}
