using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Transport
{
    public interface IListener
    {
        void RegisterEndpoint(IPEndPoint endPoint, Action<ITransport> clientFactory);
        void Start();
        void Stop();
    }

    public interface ITransport
    {
        IObservable<byte[]> Input { get; }
        IObserver<byte[]> Output { get; }
        IPEndPoint PeerEndPoint { get; }
    }
}
