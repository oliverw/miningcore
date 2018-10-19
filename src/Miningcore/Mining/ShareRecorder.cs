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
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Newtonsoft.Json;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Contract = Miningcore.Contracts.Contract;
using Share = Miningcore.Blockchain.Share;

namespace Miningcore.Mining
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
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(jsonSerializerSettings, nameof(jsonSerializerSettings));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.cf = cf;
            this.mapper = mapper;
            this.jsonSerializerSettings = jsonSerializerSettings;
            this.clock = clock;
            this.messageBus = messageBus;

            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;

            BuildFaultHandlingPolicy();
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly IShareRepository shareRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly IMasterClock clock;
        private readonly IMessageBus messageBus;
        private ClusterConfig clusterConfig;
        private readonly IMapper mapper;

        private IAsyncPolicy faultPolicy;
        private bool hasLoggedPolicyFallbackFailure;
        private IDisposable queueSub;
        private string recoveryFilename;
        private const int RetryCount = 3;
        private const string PolicyContextKeyShares = "share";
        private bool notifiedAdminOnPolicyFallback = false;

        private async Task PersistSharesAsync(IList<Share> shares)
        {
            var context = new Dictionary<string, object> { { PolicyContextKeyShares, shares } };

            await faultPolicy.ExecuteAsync(ctx => PersistSharesCoreAsync((IList<Share>) ctx[PolicyContextKeyShares]), context);
        }

        private async Task PersistSharesCoreAsync(IList<Share> shares)
        {
            await cf.RunTx(async (con, tx) =>
            {
                // Insert shares
                var mapped = shares.Select(mapper.Map<Persistence.Model.Share>).ToArray();
                await shareRepo.BatchInsertAsync(con, tx, mapped);

                // Insert blocks
                foreach (var share in shares)
                {
                    if (!share.IsBlockCandidate)
                        continue;

                    var blockEntity = mapper.Map<Block>(share);
                    blockEntity.Status = BlockStatus.Pending;
                    await blockRepo.InsertAsync(con, tx, blockEntity);

                    messageBus.SendMessage(new BlockNotification(share.PoolId, share.BlockHeight));
                }
            });
        }

        private static void OnPolicyRetry(Exception ex, TimeSpan timeSpan, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} in {timeSpan} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }

        private Task OnPolicyFallbackAsync(Exception ex, Context context)
        {
            logger.Warn(() => $"Fallback due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
            return Task.FromResult(true);
        }

        private async Task OnExecutePolicyFallbackAsync(Context context, CancellationToken ct)
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
                            await writer.WriteLineAsync(json);
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

        public async Task RecoverSharesAsync(ClusterConfig clusterConfig, string recoveryFilename)
        {
            logger.Info(() => $"Recovering shares using {recoveryFilename} ...");

            try
            {
                var successCount = 0;
                var failCount = 0;
                const int bufferSize = 100;

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
                                    await PersistSharesCoreAsync(shares);

                                    successCount += shares.Count;
                                    shares.Clear();
                                }
                            }

                            catch(Exception ex)
                            {
                                logger.Error(ex, () => $"Unable to import shares");
                                failCount++;
                            }

                            // progress
                            var now = DateTime.UtcNow;
                            if (now - lastProgressUpdate > TimeSpan.FromSeconds(10))
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
                                await PersistSharesCoreAsync(shares);

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
                    logger.Info(() => $"Successfully imported {successCount} shares");
                else
                    logger.Warn(() => $"Successfully imported {successCount} shares with {failCount} failures");
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

                messageBus.SendMessage(new AdminNotification("Share Recorder Policy Fallback",
                    $"The Share Recorder's Policy Fallback has been engaged. Check share recovery file {recoveryFilename}."));
            }
        }

        #region API-Surface

        public void Start(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;

            ConfigureRecovery();
            StartQueue();

            logger.Info(() => "Online");
        }

        public void Stop()
        {
            logger.Info(() => "Stopping ..");

            queueSub?.Dispose();

            logger.Info(() => "Stopped");
        }

        #endregion // API-Surface

        private void StartQueue()
        {
            queueSub = messageBus.Listen<ClientShare>()
                .ObserveOn(TaskPoolScheduler.Default)
                .Select(x=> x.Share)
                .Buffer(TimeSpan.FromSeconds(5), 200)
                .Where(shares => shares.Any())
                .Select(shares => Observable.FromAsync(async () =>
                {
                    try
                    {
                        await PersistSharesAsync(shares);
                    }

                    catch (Exception ex)
                    {
                        logger.Error(ex);
                    }
                }))
                .Concat()
                .Subscribe(
                    _=> {},
                    ex=> logger.Fatal(()=> $"{nameof(ShareRecorder)} queue terminated with {ex}"),
                    ()=> logger.Info(()=> $"{nameof(ShareRecorder)} queue completed"));
        }

        private void ConfigureRecovery()
        {
            recoveryFilename = !string.IsNullOrEmpty(clusterConfig.PaymentProcessing?.ShareRecoveryFile)
                ? clusterConfig.PaymentProcessing.ShareRecoveryFile
                : "recovered-shares.txt";
        }

        private void BuildFaultHandlingPolicy()
        {
            // retry with increasing delay (1s, 2s, 4s etc)
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    OnPolicyRetry);

            // after retries failed several times, break the circuit and fall through to
            // fallback action for one minute, not attempting further retries during that period
            var breaker = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .CircuitBreakerAsync(2, TimeSpan.FromMinutes(1));

            var fallback = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .FallbackAsync(OnExecutePolicyFallbackAsync, OnPolicyFallbackAsync);

            var fallbackOnBrokenCircuit = Policy
                .Handle<BrokenCircuitException>()
                .FallbackAsync(OnExecutePolicyFallbackAsync, (ex, context) => Task.FromResult(true));

            faultPolicy = Policy.WrapAsync(
                fallbackOnBrokenCircuit,
                Policy.WrapAsync(fallback, breaker, retry));
        }
    }
}
