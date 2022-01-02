using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Util;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Blockchain;

public abstract class JobManagerBase<TJob>
{
    protected JobManagerBase(IComponentContext ctx, IMessageBus messageBus)
    {
        Contract.RequiresNonNull(ctx, nameof(ctx));
        Contract.RequiresNonNull(messageBus, nameof(messageBus));

        this.ctx = ctx;
        this.messageBus = messageBus;
    }

    protected readonly IComponentContext ctx;
    protected readonly IMessageBus messageBus;
    protected ClusterConfig clusterConfig;

    protected TJob currentJob;
    private int jobId;
    protected readonly object jobLock = new();
    protected ILogger logger;
    protected PoolConfig poolConfig;
    protected bool hasInitialBlockTemplate = false;
    protected readonly Subject<Unit> blockFoundSubject = new();

    protected abstract void ConfigureDaemons();

    protected virtual async Task StartDaemonAsync(CancellationToken ct)
    {
        while(!await AreDaemonsHealthyAsync(ct))
        {
            logger.Info(() => "Waiting for daemons to come online ...");

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        logger.Info(() => "All daemons online");

        while(!await AreDaemonsConnectedAsync(ct))
        {
            logger.Info(() => "Waiting for daemons to connect to peers ...");

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    protected string NextJobId(string format = null)
    {
        Interlocked.Increment(ref jobId);
        var value = Interlocked.CompareExchange(ref jobId, 0, int.MinValue);

        if(format != null)
            return value.ToString(format);

        return value.ToStringHex8();
    }

    protected IObservable<string> BtStreamSubscribe(ZmqPubSubEndpointConfig config)
    {
        return messageBus.Listen<BtStreamMessage>()
            .Where(x => x.Topic == config.Topic)
            .SafeDo(x => messageBus.SendMessage(new TelemetryEvent(poolConfig.Id, TelemetryCategory.BtStream, x.Received - x.Sent)), logger)
            .Select(x => x.Payload);
    }

    protected virtual void OnBlockFound()
    {
        blockFoundSubject.OnNext(Unit.Default);
    }

    protected abstract Task<bool> AreDaemonsHealthyAsync(CancellationToken ct);
    protected abstract Task<bool> AreDaemonsConnectedAsync(CancellationToken ct);
    protected abstract Task EnsureDaemonsSynchedAsync(CancellationToken ct);
    protected abstract Task PostStartInitAsync(CancellationToken ct);

    #region API-Surface

    public virtual void Configure(PoolConfig pc, ClusterConfig cc)
    {
        Contract.RequiresNonNull(pc, nameof(pc));
        Contract.RequiresNonNull(cc, nameof(cc));

        logger = LogUtil.GetPoolScopedLogger(typeof(JobManagerBase<TJob>), pc);
        poolConfig = pc;
        clusterConfig = cc;

        ConfigureDaemons();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        Contract.RequiresNonNull(poolConfig, nameof(poolConfig));

        logger.Info(() => "Starting Job Manager ...");

        await StartDaemonAsync(ct);
        await EnsureDaemonsSynchedAsync(ct);
        await PostStartInitAsync(ct);

        logger.Info(() => "Job Manager Online");
    }

    #endregion // API-Surface
}
