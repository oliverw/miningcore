using System;
using System.Net;
using System.Threading;
using Autofac;
using Miningcore.Banning;
using Miningcore.Configuration;
using Xunit;

namespace Miningcore.Tests.Banning;

public class IntegratedBanManagerTests : TestBase
{
    private static readonly IPAddress address = IPAddress.Parse("192.168.1.1");

    [Fact]
    public void Ban_Valid_Address()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        Assert.False(manager.IsBanned(address));
        manager.Ban(address, TimeSpan.FromSeconds(1));
        Assert.True(manager.IsBanned(address));

        // let it expire
        Thread.Sleep(TimeSpan.FromSeconds(2));
        Assert.False(manager.IsBanned(address));
    }

    [Fact]
    public void Throw_Invalid_Duration()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        Assert.ThrowsAny<ArgumentException>(() => manager.Ban(address, TimeSpan.Zero));
    }

    [Fact]
    public void Dont_Ban_Loopback()
    {
        var manager = ModuleInitializer.Container.ResolveKeyed<IBanManager>(BanManagerKind.Integrated);

        manager.Ban(IPAddress.Loopback, TimeSpan.FromSeconds(1));
        Assert.False(manager.IsBanned(address));

        manager.Ban(IPAddress.IPv6Loopback, TimeSpan.FromSeconds(1));
        Assert.False(manager.IsBanned(address));
    }
}
