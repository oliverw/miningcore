using System;
using System.Collections.Generic;
using System.Text;

namespace MiningCore.Tests
{
    public abstract class TestBase
    {
        protected TestBase()
        {
            ModuleInitializer.Initialize();
        }
    }
}
