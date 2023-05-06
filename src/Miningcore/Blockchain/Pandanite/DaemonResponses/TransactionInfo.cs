using System;

namespace Miningcore.Blockchain.Pandanite {
    public class TransactionInfo {
        public string signature { get; set; }
        public string signingKey { get; set; }
        public string timestamp { get; set; }
        public string to { get; set; }
        public string from { get; set; }
        public ulong amount { get; set; }
        public ulong fee { get; set; }
    }
}
