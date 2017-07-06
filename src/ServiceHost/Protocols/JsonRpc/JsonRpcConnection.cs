using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using MiningCore.Transport;

// http://www.jsonrpc.org/specification
// https://github.com/Astn/JSON-RPC.NET
//
// A Notification is a Request object without an "id" member.

namespace MiningCore.Protocols.JsonRpc
{
    public class JsonRpcConnection : IJsonRpcConnection
    {
        public JsonRpcConnection(IConnection source)
        {
            
        }

        #region Implementation of IJsonRpcConnection

        public IObservable<JsonRpcRequest> Input { get; }
        public IObserver<JsonRpcResponse> Output { get; }
        public IPEndPoint RemoteEndPoint { get; }
        public string ConnectionId { get; }

        public void Close()
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
