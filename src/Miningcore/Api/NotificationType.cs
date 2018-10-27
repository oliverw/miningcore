using System;
using System.Collections.Generic;
using System.Text;

namespace Miningcore.Api
{
    public enum NotificationType
    {
        Greeting,
        BlockFound,
        NewChainHeight,
        Payment,
        BlockUnlocked,
        BlockUnlockProgress
    }
}
