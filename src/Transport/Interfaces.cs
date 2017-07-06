using System;
using System.Net;

namespace MiningCore.Transport
{
    public interface IEndpointDispatcher
    {
        /// <summary>
        /// Unique endpoint id
        /// </summary>
        string EndpointId { get; set; }

        /// <summary>
        /// Starts the dispatcher on the specified endpoint, dispatching incoming connections through the specified factory
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="connectionHandlerFactory"></param>
        void Start(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory);

        /// <summary>
        /// Stops handling incoming connections
        /// </summary>
        void Stop();
    }

    public interface IConnection
    {
        /// <summary>
        /// Observable sequence representing incoming data from remote peer
        /// </summary>
        IObservable<byte[]> Input { get; }

        /// <summary>
        /// Observer for sending outgoing data to remote peer
        /// </summary>
        IObserver<byte[]> Output { get; }

        /// <summary>
        /// Endpoint of remote peer
        /// </summary>
        IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Unique connection id
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Closes the connection (can be called from any thread)
        /// </summary>
        void Close();
    }
}
