using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv.Internal.Networking;
using Microsoft.Extensions.Logging;

namespace Transport.LibUv
{
    internal class LibUvConnection : IConnection
    {
        public LibUvConnection(LibUvEndpointDispatcher parent, UvTcpHandle server,
            Action<IConnection> clientFactory)
        {
            this.parent = parent;
            this.server = server;
            this.clientFactory = clientFactory;

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
        private MemoryStream outputQueue = new MemoryStream();
        private readonly object outputQueueLock = new object();
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

                Input = Observable.Create<byte[]>(observer =>
                {
                    client.ReadStart(
                        (_, suggestedSize, state) =>
                            ((LibUvConnection)state).AllocReadBuffer(suggestedSize),
                        (_, nread, state) =>
                            ((LibUvConnection)state).OnDataAvailableForRead(nread, observer),
                        this);

                    return Disposable.Create(() =>
                    {
                        client?.ReadStop();
                    });
                })
                .Publish()
                .RefCount();

                clientFactory(this);
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
            ReleaseReadBuffer();

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

        private void OnDataAvailableForRead(int nread, IObserver<byte[]> observer)
        {
            if (nread > 0)
            {
                var buffer = new byte[nread];
                Marshal.Copy(unmanagedReadBuffer, buffer, 0, nread);

                observer.OnNext(buffer);
            }

            else
            {
                if (nread != LibuvConstants.EOF)
                {
                    parent.tracer.LogError("Connection {0}: Error {1}", connectionId, parent.uv.strerror(nread));

                    observer.OnError(parent.uv.GetError(nread));
                }

                else
                {
                    observer.OnCompleted();
                }

                Close();
            }
        }

        private void OnDataAvailableForWrite(byte[] output)
        {
            lock (outputQueueLock)
            {
                outputQueue?.Write(output, 0, output.Length);
                outputEvent?.Send();
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