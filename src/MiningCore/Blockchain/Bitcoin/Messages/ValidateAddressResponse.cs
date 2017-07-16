using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Blockchain.Bitcoin.Messages
{
    public class ValidateAddressResponse
    {
        public bool IsValid { get; set; }
        public bool IsMine { get; set; }
        public bool IsWatchOnly { get; set; }
        public bool IsScript { get; set; }
        public string Address { get; set; }
        public string PubKey { get; set; }
        public string ScriptPubKey { get; set; }
    }
}
