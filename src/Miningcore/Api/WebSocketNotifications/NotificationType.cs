namespace Miningcore.Api.WebSocketNotifications;

public enum WsNotificationType
{
    Greeting,
    BlockFound,
    NewChainHeight,
    Payment,
    BlockUnlocked,
    BlockUnlockProgress,
    HashrateUpdated
}
