using System.Collections.Concurrent;
using Autofac;

namespace Miningcore.Crypto.Hashing.Ethash;

public static class EthashFactory
{
    private static readonly ConcurrentDictionary<string, IEthashLight> cacheFull = new();

    public static IEthashLight GetEthash(IComponentContext ctx, string name)
    {
        if(name == "")
            return null;

        // check cache
        if(cacheFull.TryGetValue(name, out var result))
            return result;

        result = ctx.ResolveNamed<IEthashLight>(name);

        cacheFull.TryAdd(name, result);

        return result;
    }
}