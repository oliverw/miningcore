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

    public interface IListener
    {
        void RegisterEndpoint(IPEndPoint endPoint, Action<IConnection> connectionHandlerFactory);
        void Start();
        void Stop();
    }
}
