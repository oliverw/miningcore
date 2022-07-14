using System.Collections.Generic;
using Miningcore.Configuration;

namespace Miningcore.Tests;

public abstract class TestBase
{
    protected Dictionary<string, CoinTemplate> coinTemplates;

    protected TestBase()
    {
        ModuleInitializer.Initialize();
    }
}
