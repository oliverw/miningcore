using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Transport
{
    public interface IConnection
    {
        IObservable<byte[]> Input { get; }
        IObserver<byte[]> Output { get; }
        IPEndPoint RemoteEndPoint { get; }
    }

    public interface IEndpointDispatcher
    {
        void Start(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory);
        void Stop();
    }
}
