using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Transport.LibUv
{
    public class UvListener : IListener
    {
        public UvListener(ILogger logger)
        {
            tracer = new LibuvTrace(logger);
        }

        private readonly ILibuvTrace tracer;
        private UvLoopHandle loop;
        private UvAsyncHandle stopEvent;
        private readonly Dictionary<IPEndPoint, Action<IConnection>> endPoints =
            new Dictionary<IPEndPoint, Action<IConnection>>();
        private LibuvFunctions uv;

        public void RegisterEndpoint(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory)
        {
            endPoints[endPoint] = connectionHandlerFactory;
        }

        public void Start()
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
                    socket.Listen(LibuvConstants.ListenBacklog, OnNewConnection, state);
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

        private static void OnNewConnection(UvStreamHandle server, int status, UvException ex, object _state)
        {
            var state = (Tuple<UvListener, Action<IConnection>>)_state;
            var self = state.Item1;

            if (status >= 0)
            {
                var con = new Connection(self, (UvTcpHandle) server, state.Item2);
                con.Init();
            }

            else
                self.tracer.ConnectionError("-1", ex);
        }

        class Connection : IConnection
        {
            public Connection(UvListener parent, UvTcpHandle server,
                Action<IConnection> clientFactory)
            {
                this.parent = parent;
                this.server = server;
                this.clientFactory = clientFactory;

                Input = inputSubject.AsObservable();
                Output = Observer.Create<byte[]>(OnDataAvailableForWrite);

                outputEvent = new UvAsyncHandle(parent.tracer);
                outputEvent.Init(parent.loop, ProcessOutputQueue, null);
            }

            private string connectionId = "-1";
            private readonly UvListener parent;
            private readonly UvTcpHandle server;
            private UvTcpHandle client;
            private IntPtr unmanagedReadBuffer = IntPtr.Zero;
            private readonly Action<IConnection> clientFactory;
            private readonly ISubject<byte[]> inputSubject = new Subject<byte[]>();
            private ConcurrentQueue<byte[]> outputQueue = new ConcurrentQueue<byte[]>();
            private UvAsyncHandle outputEvent;

            #region IConnection

            public IObservable<byte[]> Input { get; private set; }
            public IObserver<byte[]> Output { get; private set; }
            public IPEndPoint RemoteEndPoint { get; private set; }

            #endregion // IConnection

            public void Init()
            {
                try
                {
                    client = new UvTcpHandle(parent.tracer);
                    client.Init(parent.loop, null);
                    server.Accept(client);

                    RemoteEndPoint = client.GetPeerIPEndPoint();
                    connectionId = CorrelationIdGenerator.GetNextId();

                    clientFactory(this);

                    client.ReadStart(
                        (_, suggestedSize, state) =>
                            ((Connection)state).AllocReadBuffer(suggestedSize),
                        (_, nread, state) =>
                            ((Connection)state).OnDataAvailableForRead(nread),
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
                if (client != null)
                {
                    client.Dispose();
                    client = null;
                }

                if (outputEvent != null)
                {
                    outputEvent.Dispose();
                    outputEvent = null;
                }

                outputQueue = null;

                // signal we are done here
                inputSubject.OnCompleted();

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
                outputQueue?.Enqueue(output);
                outputEvent?.Send();
            }

            private async void ProcessOutputQueue()
            {
                byte[] data;
                var bufferSegments = new List<ArraySegment<byte>>();

                // collect queued buffers
                while (outputQueue != null && outputQueue.TryDequeue(out data))
                    bufferSegments.Add(new ArraySegment<byte>(data));

                // write in single request
                try
                {
                    using (var req = new UvWriteReq(parent.tracer))
                    {
                        req.Init(parent.loop);

                        var segs = new ArraySegment<ArraySegment<byte>>(bufferSegments.ToArray());
                        await req.WriteAsync(client, segs);
                    }
                }

                catch (Exception ex)
                {
                    parent.tracer.ConnectionError(connectionId, ex);
                    Close();
                }
            }
        }
    }
}
