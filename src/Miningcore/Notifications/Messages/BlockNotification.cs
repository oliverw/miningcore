using Miningcore.Persistence.Model;

namespace Miningcore.Notifications.Messages
{
    public abstract class BlockNotification
    {
        protected BlockNotification(string poolId, ulong blockHeight, string symbol)
        {
            PoolId = poolId;
            BlockHeight = blockHeight;
            Symbol = symbol;
        }

        protected BlockNotification()
        {
        }

        public string PoolId { get; set; }
        public ulong BlockHeight { get; set; }
        public string Symbol { get; set; }
    }

    public class BlockFoundNotification : BlockNotification
    {
        public BlockFoundNotification(string poolId, ulong blockHeight, string symbol) : base(poolId, blockHeight, symbol)
        {
        }

        public BlockFoundNotification()
        {
        }
    }

    public class NewChainHeightNotification : BlockNotification
    {
        public NewChainHeightNotification(string poolId, ulong blockHeight, string symbol) : base(poolId, blockHeight, symbol)
        {
        }

        public NewChainHeightNotification()
        {
        }
    }

    public class BlockConfirmationProgressNotification : BlockNotification
    {
        public BlockConfirmationProgressNotification(double progress, string poolId, ulong blockHeight, string symbol) : base(poolId, blockHeight, symbol)
        {
            Progress = progress;
        }

        public BlockConfirmationProgressNotification()
        {
        }

        public double Progress { get; set; }
    }

    public class BlockUnlockedNotification : BlockNotification
    {
        public BlockUnlockedNotification(BlockStatus status, string poolId, ulong blockHeight, string blockHash, 
            string miner, string minerExplorerLink, string symbol, string explorerLink, string blockType = "block") : 
            base(poolId, blockHeight, symbol)
        {
            Status = status;
            BlockType = blockType;
            BlockHash = blockHash;
            Miner = miner;
            ExplorerLink = explorerLink;
        }

        public BlockUnlockedNotification()
        {
        }

        public BlockStatus Status { get; set; }
        public string BlockType { get; set; }
        public string BlockHash { get; set; }
        public string Miner { get; set; }
        public string ExplorerLink { get; set; }
        public string MinerExplorerLink { get; set; }
    }
}
