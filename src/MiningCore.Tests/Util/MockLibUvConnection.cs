namespace MiningCore.Tests.Util
{
    /*
    public class MockLibUvConnection : ILibUvConnection
    {
        public MockLibUvConnection(IPEndPoint remoteEndPoint = null, string connectionId = "MOCKCONN")
        {
            RemoteEndpoint = remoteEndPoint ?? new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4444);
            ConnectionId = connectionId;

            Received = ReceiveSubject.AsObservable();
            Sent = sentSubject.AsObservable();
            Closed = closedSubject.AsObservable();
        }

        private ISubject<byte[]> sentSubject { get; } = new ReplaySubject<byte[]>();
        private ISubject<Unit> closedSubject { get; } = new ReplaySubject<Unit>();

        #region ILibUvConnection

        public IObservable<byte[]> Received { get; }

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

        #endregion // ILibUvConnection

        /// <summary>
        /// Inject data into the "Received" stream
        /// </summary>
        public ISubject<byte[]> ReceiveSubject { get; } = new ReplaySubject<byte[]>();

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
        public void Receive(byte[] data)
        {
            ReceiveSubject.OnNext(data);
        }

        /// <summary>
        /// Inject data into the "Received" stream
        /// </summary>
        public void Receive(string data)
        {
            Receive(Encoding.UTF8.GetBytes(data));
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
    */
}
