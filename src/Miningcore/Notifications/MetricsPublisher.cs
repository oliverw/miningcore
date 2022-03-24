using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using NLog;
using Prometheus;
using static Miningcore.Util.ActionUtils;

namespace Miningcore.Notifications;

public class MetricsPublisher : BackgroundService
{
    public MetricsPublisher(IMessageBus messageBus)
    {
        CreateMetrics();

        this.messageBus = messageBus;
    }

    private static ILogger logger = LogManager.GetCurrentClassLogger();

    private Summary btStreamLatencySummary;
    private Counter shareCounter;
    private Summary rpcRequestDurationSummary;
    private Summary stratumRequestDurationSummary;
    private readonly IMessageBus messageBus;
    private Counter validShareCounter;
    private Counter invalidShareCounter;
    private Summary hashComputationSummary;
    private Gauge poolConnectionsGauge;
    private Gauge poolHashrateGauge;

    private void CreateMetrics()
    {
        poolConnectionsGauge = Metrics.CreateGauge("miningcore_pool_connections", "Number of connections per pool", new GaugeConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        poolHashrateGauge = Metrics.CreateGauge("miningcore_pool_hashrate", "Hashrate per pool", new GaugeConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        btStreamLatencySummary = Metrics.CreateSummary("miningcore_btstream_latency", "Latency of streaming block-templates in ms", new SummaryConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        shareCounter = Metrics.CreateCounter("miningcore_shares_total", "Received shares per pool", new CounterConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        validShareCounter = Metrics.CreateCounter("miningcore_valid_shares_total", "Valid received shares per pool", new CounterConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        invalidShareCounter = Metrics.CreateCounter("miningcore_invalid_shares_total", "Invalid received shares per pool", new CounterConfiguration
        {
            LabelNames = new[] { "pool" }
        });

        rpcRequestDurationSummary = Metrics.CreateSummary("miningcore_rpcrequest_execution_time", "Duration of RPC requests ms", new SummaryConfiguration
        {
            LabelNames = new[] { "pool", "method" }
        });

        stratumRequestDurationSummary = Metrics.CreateSummary("miningcore_stratumrequest_execution_time", "Duration of Stratum requests ms", new SummaryConfiguration
        {
            LabelNames = new[] { "pool", "method" }
        });

        hashComputationSummary = Metrics.CreateSummary("miningcore_hash_computation_time", "Duration of RPC requests ms", new SummaryConfiguration
        {
            LabelNames = new[] { "algo" }
        });
    }

    private void OnTelemetryEvent(TelemetryEvent msg)
    {
        switch(msg.Category)
        {
            case TelemetryCategory.Share:
                shareCounter.WithLabels(msg.GroupId).Inc();

                if(msg.Success.HasValue)
                {
                    if(msg.Success.Value)
                        validShareCounter.WithLabels(msg.GroupId).Inc();
                    else
                        invalidShareCounter.WithLabels(msg.GroupId).Inc();
                }
                break;

            case TelemetryCategory.BtStream:
                btStreamLatencySummary.WithLabels(msg.GroupId).Observe(msg.Elapsed.TotalMilliseconds);
                break;

            case TelemetryCategory.RpcRequest:
                rpcRequestDurationSummary.WithLabels(msg.GroupId, msg.Info).Observe(msg.Elapsed.TotalMilliseconds);
                break;

            case TelemetryCategory.StratumRequest:
                stratumRequestDurationSummary.WithLabels(msg.GroupId, msg.Info).Observe(msg.Elapsed.TotalMilliseconds);
                break;

            case TelemetryCategory.Connections:
                poolConnectionsGauge.WithLabels(msg.GroupId).Set(msg.Total);
                break;

            case TelemetryCategory.Hash:
                hashComputationSummary.WithLabels(msg.GroupId).Observe(msg.Elapsed.TotalMilliseconds);
                break;
        }
    }

    private void OnHashrateNotification(HashrateNotification msg)
    {
        poolHashrateGauge.WithLabels(msg.PoolId).Set(msg.Hashrate);
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        var telemetryEvents = messageBus.Listen<TelemetryEvent>()
            .ObserveOn(TaskPoolScheduler.Default)
            .Do(x=> Guard(()=> OnTelemetryEvent(x), ex=> logger.Error(ex.Message)))
            .Select(_=> Unit.Default);

        var hashrateNotifications = messageBus.Listen<HashrateNotification>()
            .ObserveOn(TaskPoolScheduler.Default)
            .Do(x=> Guard(()=> OnHashrateNotification(x), ex=> logger.Error(ex.Message)))
            .Select(_=> Unit.Default);

        return Observable.Merge(telemetryEvents, hashrateNotifications)
            .ToTask(ct);
    }
}
