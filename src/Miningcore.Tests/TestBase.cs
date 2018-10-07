using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Miningcore.Configuration;

namespace Miningcore.Tests
{
    public abstract class TestBase
    {
        protected Dictionary<string, CoinTemplate> coinTemplates;

        protected TestBase()
        {
            ModuleInitializer.Initialize();
        }
    }
}
