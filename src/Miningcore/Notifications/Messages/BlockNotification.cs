using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Notifications.Messages
{
    public class BlockNotification
    {
        public BlockNotification(string poolId, long blockHeight)
        {
            PoolId = poolId;
            BlockHeight = blockHeight;
        }

        public string PoolId { get; }
        public long BlockHeight { get; }
    }
}
