using System;
using System.Buffers;
using System.Diagnostics;
using System.Reflection;
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
using Newtonsoft.Json.Linq;
using NLog;
using Xunit;

namespace Miningcore.Tests.Stratum;

public class StratumConnectionTests : TestBase
{
    private const string JsonRpcVersion = "2.0";

    [Fact]
    public async Task ProcessRequest()
    {
        const string requestString = "{\"params\": [\"slush.miner1\", \"password\"], \"id\": 42, \"method\": \"mining.authorize\"}\\n";

        var rmsm = ModuleInitializer.Container.Resolve<RecyclableMemoryStreamManager>();
        var clock = ModuleInitializer.Container.Resolve<IMasterClock>();
        var logger = new NullLogger(LogManager.LogFactory);
        var connection = new StratumConnection(logger, rmsm, clock, CorrelationIdGenerator.GetNextId());
        var privCon = new PrivateObject(connection);

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

        await (Task) privCon.Invoke("ProcessRequestAsync",
            CancellationToken.None,
            onRequestAsync,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(requestString)));
    }
}
