using System;
using MiningCore.Configuration;

namespace MiningCore.Persistence.Model
{
    public class Block
    {
        public long Id { get; set; }
        public string PoolId { get; set; }
        public ulong Blockheight { get; set; }
        public BlockStatus Status { get; set; }
        public string TransactionConfirmationData { get; set; }
        public decimal Reward { get; set; }
        public DateTime Created { get; set; }
    }
}