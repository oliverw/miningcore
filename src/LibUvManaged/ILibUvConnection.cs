using System;
using System.Net;

namespace LibUvManaged
{
    public interface ILibUvConnection
    {
        IObservable<byte[]> Received { get; }
        void Send(byte[] data);
        void Close();

        IPEndPoint RemoteEndpoint { get; }
        string ConnectionId { get; }
    }
}
