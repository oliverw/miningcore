using System.Collections.Concurrent;
using Autofac;

namespace Miningcore.Crypto.Hashing.Ethash;

public static class EthashFactory
{
    private static readonly ConcurrentDictionary<string, IEthashFull> cacheFull = new();

    public static IEthashFull GetEthashFull(IComponentContext ctx, string name)
    {
        if(name == "")
            return null;

        // check cache
        if(cacheFull.TryGetValue(name, out var result))
            return result;

        result = ctx.ResolveNamed<IEthashFull>(name);

        cacheFull.TryAdd(name, result);

        return result;
    }
}