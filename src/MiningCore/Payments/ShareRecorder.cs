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
using Autofac.Features.Metadata;
using AutoMapper;
using MiningCore.Blockchain;
using MiningCore.Configuration;
using MiningCore.Extensions;
using MiningCore.Mining;
using MiningCore.Notifications;
using MiningCore.Persistence;
using MiningCore.Persistence.Model;
using MiningCore.Persistence.Repositories;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Contract = MiningCore.Contracts.Contract;

namespace MiningCore.Payments
{
    /// <summary>
    /// Asynchronously persist shares produced by all pools for processing by coin-specific payment processor(s)
    /// </summary>
    public class ShareRecorder
    {
        public ShareRecorder(IConnectionFactory cf, IMapper mapper,
            JsonSerializerSettings jsonSerializerSettings,
            IShareRepository shareRepo, IBlockRepository blockRepo,
            NotificationService notificationService)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));
            Contract.RequiresNonNull(notificationService, nameof(notificationService));

            this.cf = cf;
            this.mapper = mapper;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.notificationService = notificationService;

            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IBlockRepository blockRepo;

        private readonly IConnectionFactory cf;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly NotificationService notificationService;
        private ClusterConfig clusterConfig;
        private readonly IMapper mapper;
        private readonly BlockingCollection<IShare> queue = new BlockingCollection<IShare>();

        private readonly int QueueSizeWarningThreshold = 1024;
        private readonly IShareRepository shareRepo;
        private Policy faultPolicy;
        private bool hasLoggedPolicyFallbackFailure;
        private bool hasWarnedAboutBacklogSize;
        private IDisposable queueSub;
        private string recoveryFilename;
        private const int RetryCount = 3;
        private const string PolicyContextKeyShares = "share";
        private bool notifiedAdminOnPolicyFallback = false;

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        private void PersistSharesFaulTolerant(IList<IShare> shares)
        {
            var context = new Dictionary<string, object> { { PolicyContextKeyShares, shares } };

            faultPolicy.Execute(() => { PersistShares(shares); }, context);
        }

        private void PersistShares(IList<IShare> shares)
        {
            cf.RunTx((con, tx) =>
            {
                foreach(var share in shares)
                {
                    var shareEntity = mapper.Map<Share>(share);
                    shareRepo.Insert(con, tx, shareEntity);

                    if (share.IsBlockCandidate)
                    {
                        var blockEntity = mapper.Map<Block>(share);
                        blockEntity.Status = BlockStatus.Pending;
                        blockRepo.Insert(con, tx, blockEntity);

                        // Queue notification
                        if (clusterConfig.Notifications?.Admin?.Enabled == true &&
                            clusterConfig.Notifications?.Admin?.NotifyBlockFound == true)
                            notificationService.NotifyAdmin("Block Notification", $"Pool {share.PoolId} found block candidate {share.BlockHeight}");
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
            var shares = (IList<IShare>) context[PolicyContextKeyShares];

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
                        var shares = new List<IShare>();
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
                                var share = JsonConvert.DeserializeObject<ShareBase>(line, jsonSerializerSettings);
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

        #region API-Surface

        public void AttachPool(IMiningPool pool)
        {
            pool.Shares.Subscribe(x => { queue.Add(x.Share); });
        }

        public void Start(ClusterConfig clusterConfig)
        {
            ConfigureRecovery(clusterConfig);
            InitializeQueue();

            logger.Info(() => "Online");
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            queueSub?.Dispose();
            queueSub = null;

            logger.Info(() => "Stopped");
        }

        private void InitializeQueue()
        {
            queueSub = queue.GetConsumingEnumerable()
                .ToObservable(TaskPoolScheduler.Default)
                .Do(_ => CheckQueueBacklog())
                .Buffer(TimeSpan.FromSeconds(1), 20)
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

        private void ConfigureRecovery(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

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

        #endregion // API-Surface
    }
}
