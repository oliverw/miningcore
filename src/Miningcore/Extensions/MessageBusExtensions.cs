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
        public static void NotifyBlockFound(this IMessageBus messageBus, string poolId, ulong blockHeight, string symbol)
        {
            messageBus.SendMessage(new BlockFoundNotification(poolId, blockHeight, symbol));
        }

        public static void NotifyBlockConfirmationProgress(this IMessageBus messageBus, string poolId, Block block, CoinTemplate coin)
        {
            messageBus.SendMessage(new BlockConfirmationProgressNotification(block.ConfirmationProgress, poolId, block.BlockHeight, coin.Symbol));
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

            messageBus.SendMessage(new BlockUnlockedNotification(block.Status, poolId,
                block.BlockHeight, block.Hash, block.Miner, minerExplorerLink, coin.Symbol, blockExplorerLink, block.Type));
        }
    }
}
