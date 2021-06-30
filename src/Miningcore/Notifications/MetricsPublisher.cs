using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Prometheus;

namespace Miningcore.Notifications
{
    public class MetricsPublisher : BackgroundService
    {
        public MetricsPublisher(
            IMessageBus messageBus)
        {
            CreateMetrics();

            this.messageBus = messageBus;
        }

        private Summary btStreamLatencySummary;
        private Counter shareCounter;
        private Summary rpcRequestDurationSummary;
        private readonly IMessageBus messageBus;

        private void CreateMetrics()
        {
            btStreamLatencySummary = Metrics.CreateSummary("miningcore_btstream_latency", "Latency of streaming block-templates in ms", new SummaryConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            shareCounter = Metrics.CreateCounter("miningcore_valid_shares_total", "Valid received shares per pool", new CounterConfiguration
            {
                LabelNames = new[] { "pool" }
            });

            rpcRequestDurationSummary = Metrics.CreateSummary("miningcore_rpcrequest_execution_time", "Duration of RPC requests ms", new SummaryConfiguration
            {
                LabelNames = new[] { "pool", "method" }
            });
        }

        private void OnTelemetryEvent(TelemetryEvent msg)
        {
            switch(msg.Category)
            {
                case TelemetryCategory.Share:
                    shareCounter.WithLabels(msg.PoolId).Inc();
                    break;

                case TelemetryCategory.BtStream:
                    btStreamLatencySummary.WithLabels(msg.PoolId).Observe(msg.Elapsed.TotalMilliseconds);
                    break;

                case TelemetryCategory.RpcRequest:
                    rpcRequestDurationSummary.WithLabels(msg.PoolId, msg.Info).Observe(msg.Elapsed.TotalMilliseconds);
                    break;
            }
        }

        protected override Task ExecuteAsync(CancellationToken ct)
        {
            return messageBus
                .Listen<TelemetryEvent>()
                .Do(OnTelemetryEvent)
                .ToTask(ct);
        }
    }
}
