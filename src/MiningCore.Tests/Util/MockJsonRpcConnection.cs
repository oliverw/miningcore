using System;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using MiningCore.JsonRpc;

namespace MiningCore.Tests.Util
{
    public class MockJsonRpcConnection : IJsonRpcConnection
    {
        public MockJsonRpcConnection(IPEndPoint remoteEndPoint = null, string connectionId = "MOCKCONN")
        {
            RemoteEndpoint = remoteEndPoint ?? new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4444);
            ConnectionId = connectionId;

            Received = ReceiveSubject.AsObservable().Timestamp();
            Sent = sentSubject.AsObservable();
            Closed = closedSubject.AsObservable();
        }

        private ISubject<byte[]> sentSubject { get; } = new ReplaySubject<byte[]>();
        private ISubject<Unit> closedSubject { get; } = new ReplaySubject<Unit>();

        #region ILibUvConnection

        public IObservable<Timestamped<JsonRpcRequest>> Received { get; }

        public void Send(byte[] data)
        {
            sentSubject.OnNext(data);
        }

        public void Close()
        {
            closedSubject.OnNext(Unit.Default);
        }

        public IPEndPoint RemoteEndpoint { get; }
        public string ConnectionId { get; }

        public void Send<T>(JsonRpcResponse<T> response)
        {
            throw new NotImplementedException();
        }

        public void Send<T>(JsonRpcRequest<T> request)
        {
            throw new NotImplementedException();
        }

        #endregion // ILibUvConnection

        /// <summary>
        /// Inject data into the "Received" stream
        /// </summary>
        public ISubject<JsonRpcRequest> ReceiveSubject { get; } = new ReplaySubject<JsonRpcRequest>();

        /// <summary>
        /// Allows observing data sent through the connection
        /// </summary>
        public IObservable<byte[]> Sent { get; }

        /// <summary>
        /// Allows observing data sent through the connection
        /// </summary>
        public IObservable<Unit> Closed { get; }

        /// <summary>
        /// Inject data into the "Received" stream
        /// </summary>
        public void Receive(JsonRpcRequest data)
        {
            ReceiveSubject.OnNext(data);
        }

        /// <summary>
        /// Inject EOF into the "Received" stream
        /// </summary>
        public void ReceiveEof()
        {
            ReceiveSubject.OnCompleted();
        }

        /// <summary>
        /// Inject error into the "Received" stream
        /// </summary>
        public void ReceiveError(Exception ex)
        {
            ReceiveSubject.OnError(ex);
        }
    }
}
