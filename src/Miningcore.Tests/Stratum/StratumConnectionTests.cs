using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Miningcore.JsonRpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Xunit;

namespace Miningcore.Tests.Stratum;

public class StratumConnectionTests : TestBase
{
    private const string JsonRpcVersion = "2.0";

    private const string requestString = "{\"params\": [\"slush.miner1\", \"password\"], \"id\": 42, \"method\": \"mining.authorize\"}\\n";

    private static readonly RecyclableMemoryStreamManager rmsm = ModuleInitializer.Container.Resolve<RecyclableMemoryStreamManager>();
    private static readonly IMasterClock clock = ModuleInitializer.Container.Resolve<IMasterClock>();
    private static readonly ILogger logger = new NullLogger(LogManager.LogFactory);

    [Fact]
    public async Task ProcessRequest_Handle_Valid_Request()
    {
        var connection = new StratumConnection(logger, rmsm, clock, CorrelationIdGenerator.GetNextId());
        var wrapper = new PrivateObject(connection);

        Task onRequestAsync(StratumConnection _con, JsonRpcRequest request, CancellationToken ct)
        {
            Assert.Equal(request.JsonRpc, JsonRpcVersion);
            Assert.Equal((long) request.Id, 42);
            Assert.Equal(request.Method, "mining.authorize");
            Assert.True(request.Params is JArray);
            Assert.Equal(request.ParamsAs<JArray>().Count, 2);
            Assert.Equal(request.ParamsAs<JArray>()[0], "slush.miner1");
            Assert.Equal(request.ParamsAs<JArray>()[1], "password");

            return Task.CompletedTask;
        }

        await (Task) wrapper.Invoke("ProcessRequestAsync",
            CancellationToken.None,
            onRequestAsync,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(requestString)));
    }

    [Fact]
    public async Task ProcessRequest_Throw_On_Unparseable_Request()
    {
        const string invalidRequestString = "foo bar\\n";

        var connection = new StratumConnection(logger, rmsm, clock, CorrelationIdGenerator.GetNextId());
        var wrapper = new PrivateObject(connection);
        var callCount = 0;

        Task onRequestAsync(StratumConnection _con, JsonRpcRequest request, CancellationToken ct)
        {
            callCount++;
            return Task.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<JsonException>(()=> (Task) wrapper.Invoke("ProcessRequestAsync",
            CancellationToken.None,
            onRequestAsync,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(invalidRequestString))));

        Assert.Equal(callCount, 0);
    }

    [Fact]
    public async Task ProcessRequest_Honor_CancellationToken()
    {
        var connection = new StratumConnection(logger, rmsm, clock, CorrelationIdGenerator.GetNextId());
        var wrapper = new PrivateObject(connection);

        async Task onRequestAsync(StratumConnection _con, JsonRpcRequest request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<TaskCanceledException>(()=> (Task) wrapper.Invoke("ProcessRequestAsync",
            cts.Token,
            onRequestAsync,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(requestString))));
    }
}
