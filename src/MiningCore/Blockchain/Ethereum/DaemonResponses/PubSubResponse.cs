using System;
using System.Collections.Generic;
using System.Text;
using MiningCore.JsonRpc;

namespace MiningCore.Blockchain.Ethereum.DaemonResponses
{
    public class PubSubParams<T>
    {
        public string Subscription { get; set; }
        public T Result { get; set; }
    }

    public class PubSubResponse<T> : JsonRpcRequest<PubSubParams<T>>
    {
    }
}
