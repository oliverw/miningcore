using Miningcore.Blockchain;
using Miningcore.Messaging;
using Miningcore.Persistence.Model;
using System.Globalization;
using Miningcore.Notifications.Messages;
using Miningcore.Configuration;

namespace Miningcore.Extensions
{
    public static class MessageBusExtensions
    {
        public static void NotifyBlockFound(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            // miner account explorer link
            string minerExplorerLink = null;

            if (!string.IsNullOrEmpty(coin.ExplorerAccountLink))
                minerExplorerLink = string.Format(coin.ExplorerAccountLink, block.Miner);

            messageBus.SendMessage(new BlockFoundNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                Symbol = coin.Symbol,
                Miner = block.Miner,
                MinerExplorerLink = minerExplorerLink,
            });
        }

        public static void NotifyBlockConfirmationProgress(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            messageBus.SendMessage(new BlockConfirmationProgressNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                Symbol = coin.Symbol,
                Progress = block.ConfirmationProgress,
            });
        }

        public static void NotifyBlockUnlocked(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            // build explorer link
            string blockExplorerLink = null;
            string minerExplorerLink = null;

            if (block.Status != BlockStatus.Orphaned)
            {
                // block explorer link
                if (coin.ExplorerBlockLinks.TryGetValue(!string.IsNullOrEmpty(block.Type) ? block.Type : "block", out var blockInfobaseUrl))
                {
                    if (blockInfobaseUrl.Contains(CoinMetaData.BlockHeightPH))
                        blockExplorerLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHeightPH, block.BlockHeight.ToString(CultureInfo.InvariantCulture));
                    else if (blockInfobaseUrl.Contains(CoinMetaData.BlockHashPH) && !string.IsNullOrEmpty(block.Hash))
                        blockExplorerLink = blockInfobaseUrl.Replace(CoinMetaData.BlockHashPH, block.Hash);
                }

                // miner account explorer link
                if (!string.IsNullOrEmpty(coin.ExplorerAccountLink))
                    minerExplorerLink = string.Format(coin.ExplorerAccountLink, block.Miner);
            }

            messageBus.SendMessage(new BlockUnlockedNotification
            {
                PoolId = poolId,
                BlockHeight = block.BlockHeight,
                BlockType = block.Type,
                Symbol = coin.Symbol,
                Status = block.Status,
                BlockHash = block.Hash,
                ExplorerLink = blockExplorerLink,
                Miner = block.Miner,
                MinerExplorerLink = minerExplorerLink,
            });
        }

        public static void NotifyChainHeight(this IMessageBus messageBus, string poolId, ulong height, CoinTemplate coin)
        {
            messageBus.SendMessage(new NewChainHeightNotification
            {
                PoolId = poolId,
                BlockHeight = height,
                Symbol = coin.Symbol,
            });
        }

        public static void NotifyHashrateUpdated(this IMessageBus messageBus, string poolId, double hashrate, string miner = null, string worker = null)
        {
            messageBus.SendMessage(new HashrateNotification
            {
                PoolId = poolId,
                Hashrate = hashrate,
                Miner = miner,
                Worker = worker,
            });
        }
    }
}
