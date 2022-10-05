using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Miningcore.JsonRpc;
using Miningcore.Stratum;
using Miningcore.Time;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NSubstitute;
using Xunit;

#pragma warning disable 8974

namespace Miningcore.Tests.Stratum;

public class StratumConnectionTests : TestBase
{
    private const string JsonRpcVersion = "2.0";
    private const string ConnectionId = "foo";
    private const string requestString = "{\"params\": [\"slush.miner1\", \"password\"], \"id\": 42, \"method\": \"mining.authorize\"}\\n";
    private const string ProcessRequestAsyncMethod = "ProcessRequestAsync";

    private static readonly RecyclableMemoryStreamManager rmsm = ModuleInitializer.Container.Resolve<RecyclableMemoryStreamManager>();
    private static readonly IMasterClock clock = ModuleInitializer.Container.Resolve<IMasterClock>();
    private static readonly ILogger logger = new NullLogger(LogManager.LogFactory);

    [Fact]
    public async Task ProcessRequest_Handle_Valid_Request()
    {
        var connection = new StratumConnection(logger, rmsm, clock, ConnectionId, false);
        var wrapper = new PrivateObject(connection);

        Task handler(StratumConnection con, JsonRpcRequest request, CancellationToken ct)
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

        await (Task) wrapper.Invoke(ProcessRequestAsyncMethod,
            CancellationToken.None,
            handler,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(requestString)));
    }

    [Fact]
    public async Task ProcessRequest_Throw_On_Unparseable_Request()
    {
        const string invalidRequestString = "foo bar\\n";

        var connection = new StratumConnection(logger, rmsm, clock, ConnectionId, false);
        var wrapper = new PrivateObject(connection);
        var callCount = 0;

        Task handler(StratumConnection con, JsonRpcRequest request, CancellationToken ct)
        {
            callCount++;
            return Task.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<JsonException>(()=> (Task) wrapper.Invoke(ProcessRequestAsyncMethod,
            CancellationToken.None,
            handler,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(invalidRequestString))));

        Assert.Equal(callCount, 0);
    }

    [Fact]
    public async Task ProcessRequest_Honor_CancellationToken()
    {
        var connection = new StratumConnection(logger, rmsm, clock, ConnectionId, false);
        var wrapper = new PrivateObject(connection);
        var callCount = 0;

        async Task handler(StratumConnection con, JsonRpcRequest request, CancellationToken ct)
        {
            callCount++;

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<TaskCanceledException>(()=> (Task) wrapper.Invoke(ProcessRequestAsyncMethod,
            cts.Token,
            handler,
            new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(requestString))));

        Assert.Equal(callCount, 1);
    }

    // [Fact]
    // public async Task DetectSslHandshake_Positive()
    // {
    //     const string MethodName = "DetectSslHandshake";
    //
    //     var connection = new StratumConnection(logger, rmsm, clock, ConnectionId);
    //     var wrapper = new PrivateObject(connection);
    //
    //     var socket = Substitute.For<Socket>(SocketType.Stream, ProtocolType.Tcp);
    //     var buf = new byte[1];
    //
    //     socket.ReceiveAsync(buf, SocketFlags.Peek, CancellationToken.None).ReturnsForAnyArgs(ValueTask.FromResult(1)).AndDoes(info =>
    //     {
    //         var _buf = info.ArgAt<Memory<byte>>(0);
    //         _buf.Span[0] = 0x16;
    //     });
    //
    //     var result = await (Task<bool>) wrapper.Invoke(MethodName,
    //         socket,
    //         CancellationToken.None);
    //
    //     Assert.True(result);
    // }
}
