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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using AutoMapper;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using MiningCore.Time;
using MiningCore.Util;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NLog;
using Org.BouncyCastle.Utilities.Collections;
using Polly;
using Polly.CircuitBreaker;
using ProtoBuf;
using Contract = MiningCore.Contracts.Contract;
using Share = MiningCore.Blockchain.Share;

namespace MiningCore.Mining
{
    /// <summary>
    /// Asynchronously persist shares produced by all pools for processing by coin-specific payment processor(s)
    /// </summary>
    public class ShareRecorder
    {
        public ShareRecorder(IConnectionFactory cf, IMapper mapper,
            JsonSerializerSettings jsonSerializerSettings,
            IShareRepository shareRepo, IBlockRepository blockRepo,
            IMasterClock clock,
            NotificationService notificationService)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));

            this.cf = cf;
            this.mapper = mapper;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.clock = clock;
            this.notificationService = notificationService;

            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;

            BuildFaultHandlingPolicy();
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly IMasterClock clock;
        private readonly NotificationService notificationService;
        private ClusterConfig clusterConfig;
        private readonly IMapper mapper;
        private readonly ConcurrentDictionary<string, PoolContext> pools = new ConcurrentDictionary<string, PoolContext>();
        private BlockingCollection<Share> queue = new BlockingCollection<Share>();

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

        private readonly int QueueSizeWarningThreshold = 1024;
        private readonly TimeSpan relayReceiveTimeout = TimeSpan.FromSeconds(60);
        private readonly IShareRepository shareRepo;
        private Policy faultPolicy;
        private bool hasLoggedPolicyFallbackFailure;
        private bool hasWarnedAboutBacklogSize;
        private IDisposable queueSub;
        private string recoveryFilename;
        private const int RetryCount = 3;
        private const string PolicyContextKeyShares = "share";
        private bool notifiedAdminOnPolicyFallback = false;

        private void PersistSharesFaulTolerant(IList<Share> shares)
        {
            var context = new Dictionary<string, object> { { PolicyContextKeyShares, shares } };

            faultPolicy.Execute(() => { PersistShares(shares); }, context);
        }

        private void PersistShares(IList<Share> shares)
        {
            cf.RunTx((con, tx) =>
            {
                foreach(var share in shares)
                {
                    var shareEntity = mapper.Map<Persistence.Model.Share>(share);
                    shareRepo.Insert(con, tx, shareEntity);

                    if (share.IsBlockCandidate)
                    {
                        var blockEntity = mapper.Map<Block>(share);
                        blockEntity.Status = BlockStatus.Pending;
                        blockRepo.Insert(con, tx, blockEntity);

                        notificationService.NotifyBlock(share.PoolId, share.BlockHeight);
                    }
                }
            });
        }

        private static void OnPolicyRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} in {timeSpan} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private void OnPolicyFallback(Exception ex, Context context)
        {
            logger.Warn(() => $"Fallback due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private void OnExecutePolicyFallback(Context context)
        {
            var shares = (IList<Share>) context[PolicyContextKeyShares];

            try
            {
                using(var stream = new FileStream(recoveryFilename, FileMode.Append, FileAccess.Write))
                {
                    using(var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        if (stream.Length == 0)
                            WriteRecoveryFileheader(writer);

                        foreach(var share in shares)
                        {
                            var json = JsonConvert.SerializeObject(share, jsonSerializerSettings);
                            writer.WriteLine(json);
                        }
                    }
                }

                NotifyAdminOnPolicyFallback();
            }

            catch(Exception ex)
            {
                if (!hasLoggedPolicyFallbackFailure)
                {
                    logger.Fatal(ex, "Fatal error during policy fallback execution. Share(s) will be lost!");
                    hasLoggedPolicyFallbackFailure = true;
                }
            }
        }

        private static void WriteRecoveryFileheader(StreamWriter writer)
        {
            writer.WriteLine("# The existence of this file means shares could not be committed to the database.");
            writer.WriteLine("# You should stop the pool cluster and run the following command:");
            writer.WriteLine("# miningcore -c <path-to-config> -rs <path-to-this-file>\n");
        }

        public void RecoverShares(ClusterConfig clusterConfig, string recoveryFilename)
        {
            logger.Info(() => $"Recovering shares using {recoveryFilename} ...");

            try
            {
                var successCount = 0;
                var failCount = 0;
                const int bufferSize = 20;

                using(var stream = new FileStream(recoveryFilename, FileMode.Open, FileAccess.Read))
                {
                    using(var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        var shares = new List<Share>();
                        var lastProgressUpdate = DateTime.UtcNow;

                        while(!reader.EndOfStream)
                        {
                            var line = reader.ReadLine().Trim();

                            // skip blank lines
                            if (line.Length == 0)
                                continue;

                            // skip comments
                            if (line.StartsWith("#"))
                                continue;

                            // parse
                            try
                            {
                                var share = JsonConvert.DeserializeObject<Share>(line, jsonSerializerSettings);
                                shares.Add(share);
                            }

                            catch(JsonException ex)
                            {
                                logger.Error(ex, () => $"Unable to parse share record: {line}");
                                failCount++;
                            }

                            // import
                            try
                            {
                                if (shares.Count == bufferSize)
                                {
                                    PersistShares(shares);

                                    shares.Clear();
                                    successCount += shares.Count;
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex, () => $"Unable to import shares");
                                failCount++;
                            }

                            // progress
                            var now = DateTime.UtcNow;
                            if (now - lastProgressUpdate > TimeSpan.FromMinutes(1))
                            {
                                logger.Info($"{successCount} shares imported");
                                lastProgressUpdate = now;
                            }
                        }

                        // import remaining shares
                        try
                        {
                            if (shares.Count > 0)
                            {
                                PersistShares(shares);

                                successCount += shares.Count;
                            }
                        }

                        catch(Exception ex)
                        {
                            logger.Error(ex, () => $"Unable to import shares");
                            failCount++;
                        }
                    }
                }

                if (failCount == 0)
                    logger.Info(() => $"Successfully recovered {successCount} shares");
                else
                    logger.Warn(() => $"Successfully {successCount} shares with {failCount} failures");
            }

            catch(FileNotFoundException)
            {
                logger.Error(() => $"Recovery file {recoveryFilename} was not found");
            }
        }

        private void NotifyAdminOnPolicyFallback()
        {
            if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                clusterConfig.Notifications?.Admin?.NotifyPaymentSuccess == true &&
                !notifiedAdminOnPolicyFallback)
            {
                notifiedAdminOnPolicyFallback = true;

                notificationService.NotifyAdmin(
                    "Share Recorder Policy Fallback",
                    $"The Share Recorder's Policy Fallback has been engaged. Check share recovery file {recoveryFilename}.");
            }
        }

        private void StartExternalStratumPublisherListeners()
        {
            var stratumsByUrl = clusterConfig.Pools.Where(x => x.ExternalStratums?.Any() == true)
                .SelectMany(x => x.ExternalStratums)
                .Where(x => x.Url != null && x.Topic != null)
                .GroupBy(x =>
                {
                    var tmp = x.Url.Trim();
                    return !tmp.EndsWith("/") ? tmp : tmp.Substring(0, tmp.Length - 1);
                }, x=> x.Topic.Trim());

            var serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            foreach (var item in stratumsByUrl)
            {
                var thread = new Thread(arg =>
                {
                    var urlAndTopic = (IGrouping<string, string>) arg;
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
                                    queue.Add(share);

                                    // misc
                                    if (pools.TryGetValue(topic, out var poolContext))
                                    {
                                        var pool = poolContext.Pool;
                                        poolContext.Logger.Info(() => $"External {(!string.IsNullOrEmpty(share.Source) ? $"[{share.Source.ToUpper()}] " : string.Empty)}share accepted: D={Math.Round(share.Difficulty, 3)}");

                                        // update pool stats
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
                                }
                            }
                        }

                        catch (Exception ex)
                        {
                            logger.Error(ex);
                        }
                    }
                });

                thread.Start(item);
            }
        }

        #region API-Surface

        public void AttachPool(IMiningPool pool)
        {
            pools[pool.Config.Id] = new PoolContext(pool, LogUtil.GetPoolScopedLogger(typeof(ShareRecorder), pool.Config));

            pool.Shares.Subscribe(x => { queue.Add(x.Share); });
        }

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            ConfigureRecovery();
            InitializeQueue();
            StartExternalStratumPublisherListeners();

            logger.Info(() => "Online");
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            queueSub?.Dispose();
            queueSub = null;

            queue?.Dispose();
            queue = null;

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface

        private void InitializeQueue()
        {
            queueSub = queue.GetConsumingEnumerable()
                .ToObservable(TaskPoolScheduler.Default)
                .Do(_ => CheckQueueBacklog())
                .Buffer(TimeSpan.FromSeconds(1), 100)
                .Where(shares => shares.Any())
                .Subscribe(shares =>
                {
                    try
                    {
                        PersistSharesFaulTolerant(shares);
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }
                });
        }

        private void ConfigureRecovery()
        {
            recoveryFilename = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.ShareRecoveryFile)
                ? clusterConfig.PaymentProcessing.ShareRecoveryFile
                : "recovered-shares.txt";
        }

        private void CheckQueueBacklog()
        {
            if (queue.Count > QueueSizeWarningThreshold)
            {
                if (!hasWarnedAboutBacklogSize)
                {
                    logger.Warn(() => $"Share persistence queue backlog has crossed {QueueSizeWarningThreshold}");
                    hasWarnedAboutBacklogSize = true;
                }
            }

            else if (hasWarnedAboutBacklogSize && queue.Count <= QueueSizeWarningThreshold / 2)
            {
                hasWarnedAboutBacklogSize = false;
            }
        }

        private void BuildFaultHandlingPolicy()
        {
            // retry with increasing delay (1s, 2s, 4s etc)
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    OnPolicyRetry);

            // after retries failed several times, break the circuit and fall through to
            // fallback action for one minute, not attempting further retries during that period
            var breaker = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .CircuitBreaker(2, TimeSpan.FromMinutes(1));

            var fallback = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .Fallback(OnExecutePolicyFallback, OnPolicyFallback);

            var fallbackOnBrokenCircuit = Policy
                .Handle<BrokenCircuitException>()
                .Fallback(OnExecutePolicyFallback, (ex, context) => { });

            faultPolicy = Policy.Wrap(
                fallbackOnBrokenCircuit,
                Policy.Wrap(fallback, breaker, retry));
        }
    }
}
