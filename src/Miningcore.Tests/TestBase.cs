namespace Miningcore.Tests;

public abstract class TestBase
{
    protected TestBase()
    {
        ModuleInitializer.Initialize();
    }
}
