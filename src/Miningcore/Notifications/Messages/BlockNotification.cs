using Miningcore.Persistence.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Miningcore.Notifications.Messages
{
    public abstract class BlockNotification
    {
        protected BlockNotification(string poolId, long blockHeight, string poolCurrencySymbol)
        {
            PoolId = poolId;
            BlockHeight = blockHeight;
            PoolCurrencySymbol = poolCurrencySymbol;
        }

        public string PoolId { get; }
        public string PoolCurrencySymbol { get; }
        public long BlockHeight { get; }
    }

    public class BlockFoundNotification : BlockNotification
    {
        public BlockFoundNotification(string poolId, long blockHeight, 
            string poolCurrencySymbol) : base(poolId, blockHeight, poolCurrencySymbol)
        {
        }
    }

    public class NewChainHeightNotification : BlockNotification
    {
        public NewChainHeightNotification(string poolId, long blockHeight, 
            string poolCurrencySymbol) : base(poolId, blockHeight, poolCurrencySymbol)
        {
        }
    }

    public class BlockConfirmationProgressNotification : BlockNotification
    {
        public BlockConfirmationProgressNotification(double progress, string poolId, long blockHeight, 
            string poolCurrencySymbol) : base(poolId, blockHeight, poolCurrencySymbol)
        {
            Progress = progress;
        }

        public double Progress { get; }
    }

    public class BlockUnlockedNotification : BlockNotification
    {
        public BlockUnlockedNotification(BlockStatus status, string poolId, long blockHeight, 
            string poolCurrencySymbol, string blockType = "block") : base(poolId, blockHeight, poolCurrencySymbol)
        {
            Status = status;
            BlockType = blockType;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public BlockStatus Status { get; }

        public string BlockType { get; }
    }
}
