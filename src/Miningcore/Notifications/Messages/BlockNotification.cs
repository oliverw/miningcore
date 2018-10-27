using Miningcore.Persistence.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public abstract class BlockNotification
    {
        protected BlockNotification(string poolId, long blockHeight)
        {
            PoolId = poolId;
            BlockHeight = blockHeight;
        }

        public string PoolId { get; }
        public long BlockHeight { get; }
    }

    public class BlockFoundNotification : BlockNotification
    {
        public BlockFoundNotification(string poolId, long blockHeight) : base(poolId, blockHeight)
        {
        }
    }

    public class NewChainHeightNotification : BlockNotification
    {
        public NewChainHeightNotification(string poolId, long blockHeight) : base(poolId, blockHeight)
        {
        }
    }

    public class BlockConfirmationProgressNotification : BlockNotification
    {
        public BlockConfirmationProgressNotification(double progress, string poolId, long blockHeight) : base(poolId, blockHeight)
        {
            Progress = progress;
        }

        public double Progress { get; }
    }

    public class BlockUnlockedNotification : BlockNotification
    {
        public BlockUnlockedNotification(BlockStatus status, string poolId, long blockHeight, string blockType = "block") : base(poolId, blockHeight)
        {
            Status = status;
            BlockType = blockType;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public BlockStatus Status { get; }

        public string BlockType { get; }
    }
}
