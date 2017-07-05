using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;

// https://nikhilm.github.io/uvbook/basics.html

namespace ServiceHost
{
    class Program
    {
        public interface ITransport
        {
            IObservable<byte[]> Input { get; }
            IObserver<byte[]> Output { get; }
        }

        public interface ITransportClient
        {
        }

        public class UvListener
        {
            public UvListener(ILogger logger)
            {
                tracer = new LibuvTrace(logger);
            }

            private readonly ILibuvTrace tracer;
            private UvLoopHandle loop;
            private UvAsyncHandle stopEvent;
            private readonly Dictionary<IPEndPoint, Func<ITransport, ITransportClient>> endPoints = 
                new Dictionary<IPEndPoint, Func<ITransport, ITransportClient>>();
            private LibuvFunctions uv;

            public void RegisterEndpoint(IPEndPoint endPoint, Func<ITransport, ITransportClient> clientFactory)
            {
                endPoints[endPoint] = clientFactory;
            }

            public void Run()
            {
                try
                {
                    loop = new UvLoopHandle(tracer);

                    uv = new LibuvFunctions();
                    stopEvent = new UvAsyncHandle(tracer);

                    loop.Init(uv);

                    stopEvent.Init(loop, () =>
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        loop.Stop();
                    }, null);

                    foreach (var endPoint in endPoints.Keys)
                    {
                        var socket = new UvTcpHandle(tracer);
                        socket.Init(loop, null);
                        socket.Bind(endPoint);

                        var connectionHandler = endPoints[endPoint];
                        var state = Tuple.Create(this, connectionHandler);
                        socket.Listen(LibuvConstants.ListenBacklog, OnNewConnectionStatic, state);
                    }

                    loop.Run();

                    // close handles
                    uv.walk(loop, (handle, state) => uv.close(handle, null), IntPtr.Zero);

                    // invoke handle-close-callbacks
                    loop.Run();

                    // done
                    loop.Dispose();
                }

                catch (Exception ex)
                {
                    tracer.LogError(ex.ToString());
                    throw;
                }
            }

            public void Stop()
            {
                stopEvent?.Send();
            }

            private static void OnNewConnectionStatic(UvStreamHandle server, int status, UvException ex, object _state)
            {
                var state = (Tuple<UvListener, Func<ITransport, ITransportClient>>) _state;
                var self = state.Item1;

                if (status >= 0)
                {
                    var con = new Connection(self, server, state.Item2);
                    con.Init();
                }

                else
                    self.tracer.ConnectionError("-1", ex);
            }

            class Connection : ITransport
            {
                public Connection(UvListener parent, UvStreamHandle server, 
                    Func<ITransport, ITransportClient> clientFactory)
                {
                    this.parent = parent;
                    this.server = server;
                    this.clientFactory = clientFactory;

                    Input = inputSubject.AsObservable();
                    Output = Observer.Create<byte[]>(OnDataAvailableForWrite);
                }

                private string connectionId = "-1";
                private readonly UvListener parent;
                private readonly UvStreamHandle server;
                private UvTcpHandle client;
                private IntPtr unmanagedReadBuffer = IntPtr.Zero;
                private readonly Func<ITransport, ITransportClient> clientFactory;
                private readonly ISubject<byte[]> inputSubject = new Subject<byte[]>();

                #region ITransport

                public IObservable<byte[]> Input { get; private set; }
                public IObserver<byte[]> Output { get; private set; }

                #endregion // ITransport

                public void Init()
                {
                    try
                    {
                        client = new UvTcpHandle(parent.tracer);
                        client.Init(parent.loop, null);
                        server.Accept(client);

                        clientFactory(this);

                        client.ReadStart(
                            (_, suggestedSize, state)=> 
                                ((Connection)state).AllocReadBuffer(suggestedSize), 
                            (_, nread, state)=> 
                                ((Connection) state).OnDataAvailableForRead(nread), 
                            this);
                    }

                    catch (Exception ex)
                    {
                        parent.tracer.ConnectionError(connectionId, ex);
                        Close();
                    }
                }

                private void Close()
                {
                    server?.Dispose();
                    client?.Dispose();

                    if (unmanagedReadBuffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(unmanagedReadBuffer);
                        unmanagedReadBuffer = IntPtr.Zero;
                    }
                }

                private LibuvFunctions.uv_buf_t AllocReadBuffer(int suggestedSize)
                {
                    unmanagedReadBuffer = Marshal.AllocHGlobal(suggestedSize);
                    return parent.uv.buf_init(unmanagedReadBuffer, suggestedSize);
                }

                private void OnDataAvailableForRead(int nread)
                {
                    if (nread > 0)
                    {
                        var buffer = new byte[nread];
                        Marshal.Copy(unmanagedReadBuffer, buffer, 0, nread);
                        inputSubject.OnNext(buffer);
                    }

                    else
                    {
                        if (nread != LibuvConstants.EOF)
                        {
                            parent.tracer.LogError("Connection {0}: Error {1}", connectionId, parent.uv.strerror(nread));
                        }

                        Close();
                    }
                }

                private void OnDataAvailableForWrite(byte[] output)
                {
                }
            }
        }

        class EchoClient : ITransportClient
        {
            public EchoClient(ITransport transport)
            {
                transport.Input
                    .ObserveOn(ThreadPoolScheduler.Instance)
                    .Subscribe(x =>
                {
                    var msg = System.Text.Encoding.UTF8.GetString(x);

                    Console.WriteLine($"You wrote: {msg}");

                    transport.Output.OnNext(System.Text.Encoding.UTF8.GetBytes(msg));
                });
            }
        }

        static void Main(string[] args)
        {
            var logger = new DebugLogger("default");
            var server = new UvListener(logger);

            // handle ctrl+c
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                server.Stop();
            };

            server.RegisterEndpoint(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 57000), (transport => new EchoClient(transport)));

            server.Run();
        }
    }
}
