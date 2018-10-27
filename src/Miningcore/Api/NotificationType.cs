using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Api
{
    public enum NotificationType
    {
        BlockFound,
        NewChainHeight,
        Payment,
        BlockUnlocked,
        BlockUnlockProgress
    }
}
