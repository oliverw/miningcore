using System.IO.Compression;
using System.Reactive.Disposables;
using System.Text;
using Microsoft.Extensions.Hosting;
using Miningcore.Blockchain.Bitcoin.Configuration;
using Miningcore.Blockchain.Cryptonote.Configuration;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Time;
using NLog;
using ZeroMQ;

namespace Miningcore.Mining;

/// <summary>
/// Receives ready made block templates from GBTRelay
/// </summary>
public class BtStreamReceiver : BackgroundService
{
    public BtStreamReceiver(
        IMasterClock clock,
        IMessageBus messageBus,
        ClusterConfig clusterConfig)
    {
        Contract.RequiresNonNull(clock);
        Contract.RequiresNonNull(messageBus);

        this.clock = clock;
        this.messageBus = messageBus;
        this.clusterConfig = clusterConfig;
    }

    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IMasterClock clock;
    private readonly IMessageBus messageBus;
    private readonly ClusterConfig clusterConfig;

    private static ZSocket SetupSubSocket(ZmqPubSubEndpointConfig relay, bool silent = false)
    {
        var subSocket = new ZSocket(ZSocketType.SUB);

        if(!string.IsNullOrEmpty(relay.SharedEncryptionKey))
            subSocket.SetupCurveTlsClient(relay.SharedEncryptionKey, logger);

        subSocket.Connect(relay.Url);
        subSocket.SubscribeAll();

        if(!silent)
        {
            if(subSocket.CurveServerKey != null && subSocket.CurveServerKey.Any(x => x != 0))
                logger.Info($"Monitoring Bt-Stream source {relay.Url} using key {subSocket.CurveServerKey.ToHexString()}");
            else
                logger.Info($"Monitoring Bt-Stream source {relay.Url}");
        }

        return subSocket;
    }

    private void ProcessMessage(ZMessage msg)
    {
        // extract frames
        var topic = msg[0].ToString(Encoding.UTF8);
        var flags = msg[1].ReadUInt32();
        var data = msg[2].Read();
        var sent = DateTimeOffset.FromUnixTimeMilliseconds(msg[3].ReadInt64()).DateTime;

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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var endpoints = clusterConfig.Pools.Select(x =>
                x.Extra.SafeExtensionDataAs<BitcoinPoolConfigExtra>()?.BtStream ??
                x.Extra.SafeExtensionDataAs<CryptonotePoolConfigExtra>()?.BtStream)
            .Where(x => x != null)
            .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
            .ToArray();

        if(!endpoints.Any())
            return;

        await Task.Run(() =>
        {
            var timeout = TimeSpan.FromMilliseconds(5000);
            var reconnectTimeout = TimeSpan.FromSeconds(300);

            var relays = endpoints
                .DistinctBy(x => $"{x.Url}:{x.SharedEncryptionKey}")
                .ToArray();

            logger.Info(() => "Online");

            while(!ct.IsCancellationRequested)
            {
                // track last message received per endpoint
                var lastMessageReceived = relays.Select(_ => clock.Now).ToArray();

                try
                {
                    // setup sockets
                    var sockets = relays.Select(x=> SetupSubSocket(x)).ToArray();

                    using(new CompositeDisposable(sockets))
                    {
                        var pollItems = sockets.Select(_ => ZPollItem.CreateReceiver()).ToArray();

                        while(!ct.IsCancellationRequested)
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
                                        sockets[i] = SetupSubSocket(relays[i], true);

                                        // reset clock
                                        lastMessageReceived[i] = clock.Now;

                                        logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                    }
                                }

                                if(error != null)
                                    logger.Error(() => $"{nameof(ShareReceiver)}: {error.Name} [{error.Name}] during receive");
                            }

                            else
                            {
                                // check for timeouts
                                for(var i = 0; i < messages.Length; i++)
                                {
                                    if(clock.Now - lastMessageReceived[i] > reconnectTimeout)
                                    {
                                        // re-create socket
                                        sockets[i].Dispose();
                                        sockets[i] = SetupSubSocket(relays[i], true);

                                        // reset clock
                                        lastMessageReceived[i] = clock.Now;

                                        logger.Info(() => $"Receive timeout of {reconnectTimeout.TotalSeconds} seconds exceeded. Re-connecting to {relays[i].Url} ...");
                                    }
                                }
                            }
                        }
                    }
                }

                catch(Exception ex)
                {
                    logger.Error(() => $"{nameof(ShareReceiver)}: {ex}");

                    if(!ct.IsCancellationRequested)
                        Thread.Sleep(1000);
                }
            }

            logger.Info(() => "Offline");
        }, ct);
    }
}
