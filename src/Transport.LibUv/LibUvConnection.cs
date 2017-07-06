using System;
using System.IO;
using System.Net;
using System.Reactive;
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

            Input = inputSubject.AsObservable();
            Output = Observer.Create<byte[]>(OnDataAvailableForWrite);

            outputEvent = new UvAsyncHandle(parent.tracer);
            outputEvent.Init(parent.loop, ProcessOutputQueue, null);
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

        #region IConnection

        public IObservable<byte[]> Input { get; }
        public IObserver<byte[]> Output { get; }
        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId => connectionId;

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

                logger.Info(() => $"Accepted incoming connection [{connectionId}] from {RemoteEndPoint}");

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
                Close();
            }
        }

        private void Close()
        {
            logger.Info(() => $"Closing connection [{connectionId}]");

            // signal we are done here
            inputSubject.OnCompleted();

            if (client != null)
            {
                client.ReadStop();
                client.Dispose();
                client = null;
            }

            if (outputEvent != null)
            {
                outputEvent.Dispose();
                outputEvent = null;
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
                logger.Debug(() => $"Received {nread} bytes from [{connectionId}]");

                var buffer = new byte[nread];
                Marshal.Copy(unmanagedReadBuffer, buffer, 0, nread);
                inputSubject.OnNext(buffer);
            }

            else
            {
                if (nread != LibuvConstants.EOF)
                {
                    parent.tracer.LogError("Connection [{0}]: Error {1}", connectionId, parent.uv.strerror(nread));
                }

                else
                {
                    logger.Info(() => $"Received EOF from [{connectionId}]");
                }

                Close();
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

                    logger.Debug(() => $"Queueing {output.Length} outbound bytes for [{connectionId}] - Queue size now {outputQueue.Length}");
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
                    logger.Debug(() => $"Sending {buffer.Value.Count} bytes to [{connectionId}]");

                    using (var req = new UvWriteReq(parent.tracer))
                    {
                        req.Init(parent.loop);
                        await req.WriteAsync(client, new ArraySegment<ArraySegment<byte>>(new []{ buffer.Value }));
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