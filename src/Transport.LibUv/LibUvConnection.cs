using System;
using System.IO;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Autofac;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;
using MiningCore.Extensions;

namespace MiningCore.Transport.LibUv
{
    internal class LibUvConnection : IConnection
    {
        public LibUvConnection(IComponentContext ctx, 
            LibUvEndpointDispatcher parent, 
            UvTcpHandle server,
            Action<IConnection> clientFactory)
        {
            this.logger = ctx.Resolve<ILogger<LibUvConnection>>();
            this.parent = parent;
            this.server = server;
            this.clientFactory = clientFactory;

            Input = Observable.Create<byte[]>(observer =>
            {
                var sub = inputSubject.Subscribe(observer);

                // Close connection when nobody's listening anymore
                return new CompositeDisposable(sub, Disposable.Create(() =>
                {
                    logger.Debug(() => $"[{connectionId}] Last subscriber disconnected from input stream");

                    Close();
                }));
            })
            .Publish()
            .RefCount();

            Output = Observer.Create<byte[]>(OnDataAvailableForWrite);

            outputEvent = new UvAsyncHandle(parent.tracer);
            outputEvent.Init(parent.loop, ProcessOutputQueue, null);

            closeEvent = new UvAsyncHandle(parent.tracer);
            closeEvent.Init(parent.loop, CloseInternal, null);
        }

        private string connectionId = "-1";
        private readonly LibUvEndpointDispatcher parent;
        private readonly UvTcpHandle server;
        private UvTcpHandle client;
        private IntPtr unmanagedReadBuffer = IntPtr.Zero;
        private readonly Action<IConnection> clientFactory;
        private readonly ISubject<byte[]> inputSubject = new Subject<byte[]>();
        private MemoryStream outputQueue = new MemoryStream();
        private readonly object outputQueueLock = new object();
        private UvAsyncHandle outputEvent;
        private readonly ILogger<LibUvConnection> logger;
        private UvAsyncHandle closeEvent;

        #region IConnection

        public IObservable<byte[]> Input { get; }
        public IObserver<byte[]> Output { get; }
        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId => connectionId;

        public void Close()
        {
            // dispatch actual closing to loop thread
            closeEvent.Send();
        }

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

                logger.Info(() => $"[{connectionId}] Accepted connection from {RemoteEndPoint}");

                clientFactory(this);

                client.ReadStart(
                    (_, suggestedSize, state) =>
                        ((LibUvConnection) state).AllocReadBuffer(suggestedSize),
                    (_, nread, state) =>
                        ((LibUvConnection) state).OnDataAvailableForRead(nread),
                    this);
            }

            catch (Exception ex)
            {
                parent.tracer.ConnectionError(connectionId, ex);
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            logger.Info(() => $"[{connectionId}] Closing connection");

            // signal we are done here
            inputSubject.OnCompleted();

            if (client != null)
            {
                client.ReadStop();
                client.Close();
                client = null;
            }

            if (outputEvent != null)
            {
                outputEvent.Close();
                outputEvent = null;
            }

            if (closeEvent != null)
            {
                closeEvent.Close();
                closeEvent = null;
            }

            lock (outputQueueLock)
            {
                outputQueue = null;
            }

            ReleaseReadBuffer();
        }

        private LibuvFunctions.uv_buf_t AllocReadBuffer(int suggestedSize)
        {
            unmanagedReadBuffer = Marshal.AllocHGlobal(suggestedSize);
            return parent.uv.buf_init(unmanagedReadBuffer, suggestedSize);
        }

        private void ReleaseReadBuffer()
        {
            if (unmanagedReadBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(unmanagedReadBuffer);
                unmanagedReadBuffer = IntPtr.Zero;
            }
        }

        private void OnDataAvailableForRead(int nread)
        {
            if (nread > 0)
            {
                logger.Debug(() => $"[{connectionId}] Received {nread} bytes");

                var buffer = new byte[nread];
                Marshal.Copy(unmanagedReadBuffer, buffer, 0, nread);
                inputSubject.OnNext(buffer);
            }

            else
            {
                if (nread != LibuvConstants.EOF)
                {
                    parent.tracer.LogError("[{0}] Error {1}", connectionId, parent.uv.strerror(nread));
                }

                else
                {
                    logger.Info(() => $"[{connectionId}] Received EOF");
                }

                CloseInternal();
            }
        }

        private void OnDataAvailableForWrite(byte[] output)
        {
            lock (outputQueueLock)
            {
                if (outputQueue != null)
                {
                    outputQueue.Write(output, 0, output.Length);
                    outputEvent?.Send();

                    logger.Debug(() => $"[{connectionId}] Queueing {output.Length} bytes for transmission - Queue size now {outputQueue.Length}");
                }
            }
        }

        private async void ProcessOutputQueue()
        {
            ArraySegment<byte>? buffer = null;

            lock (outputQueueLock)
            {
                if (outputQueue.Length > 0)
                {
                    buffer = new ArraySegment<byte>(outputQueue.ToArray());
                    outputQueue.SetLength(0);
                }
            }

            // write in single request
            if (buffer.HasValue)
            {
                try
                {
                    logger.Debug(() => $"[{connectionId}] Sending {buffer.Value.Count} bytes");

                    using (var req = new UvWriteReq(parent.tracer))
                    {
                        req.Init(parent.loop);
                        await req.WriteAsync(client, new ArraySegment<ArraySegment<byte>>(new []{ buffer.Value }));
                    }
                }

                catch (Exception ex)
                {
                    parent.tracer.ConnectionError(connectionId, ex);
                    CloseInternal();
                }
            }
        }
    }
}