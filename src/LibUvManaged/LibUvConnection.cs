using System;
using System.IO;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using CodeContracts;
using LibUv;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using NLog;

namespace LibUvManaged
{
    internal class LibUvConnection : ILibUvConnection
    {
        public LibUvConnection(LibUvListener parent, UvTcpHandle server)
        {
            this.parent = parent;
            this.server = server;

            Received = Observable.Create<byte[]>(observer =>
            {
                var sub = inputSubject.Subscribe(observer);

                // Close connection when nobody's listening anymore
                return new CompositeDisposable(sub, Disposable.Create(() =>
                {
                    logger.Debug(()=> $"[{connectionId}] Last subscriber disconnected from receiver stream");

                    Close();
                }));
            })
            .Publish()
            .RefCount();

            outputEvent = new UvAsyncHandle(parent.tracer);
            outputEvent.Init(parent.loop, ProcessOutputQueue, null);

            closeEvent = new UvAsyncHandle(parent.tracer);
            closeEvent.Init(parent.loop, CloseInternal, null);
        }

        private string connectionId = "-1";
        private readonly LibUvListener parent;
        private readonly UvTcpHandle server;
        private UvTcpHandle client;
        private IntPtr unmanagedReadBuffer = IntPtr.Zero;
        private readonly ISubject<byte[]> inputSubject = new Subject<byte[]>();
        private MemoryStream outputQueue = new MemoryStream();
        private readonly object outputQueueLock = new object();
        private UvAsyncHandle outputEvent;
	    private static readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private UvAsyncHandle closeEvent;

        #region ILibUvConnection

        public IObservable<byte[]> Received { get; }
        public IPEndPoint RemoteEndPoint { get; private set; }
        public string ConnectionId => connectionId;

        public void Send(byte[] data)
        {
            Contract.RequiresNonNull(data, nameof(data));

            lock (outputQueueLock)
            {
                if (outputQueue != null)
                {
                    outputQueue.Write(data, 0, data.Length);
                    outputEvent?.Send();

					logger.Trace(() => $"[{connectionId}] Queueing {data.Length} bytes for transmission - Queue size now {outputQueue.Length}");
                }
            }
        }

        public void Close()
        {
            // dispatch actual closing to loop thread
            closeEvent?.Send();
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

				logger.Debug(() => $"[{connectionId}] Accepted connection from {RemoteEndPoint}");

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
			logger.Debug(() => $"[{connectionId}] Closing connection");

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
				logger.Trace(() => $"[{connectionId}] Received {nread} bytes");

                var buffer = new byte[nread];
                Marshal.Copy(unmanagedReadBuffer, buffer, 0, nread);
                inputSubject.OnNext(buffer);
            }

            else
            {
                if (nread != LibuvConstants.EOF)
					logger.Debug(() => $"[{connectionId}] Error {parent.uv.strerror(nread)}");
                else
					logger.Debug(() => $"[{connectionId}] Received EOF");

                CloseInternal();
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

	                logger.Trace(() => $"[{connectionId}] Queue size now {outputQueue.Length}");
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