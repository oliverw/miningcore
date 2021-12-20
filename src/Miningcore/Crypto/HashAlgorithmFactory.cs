using System.Collections.Concurrent;
using Autofac;
using Miningcore.Crypto.Hashing.Algorithms;
using Newtonsoft.Json.Linq;

namespace Miningcore.Crypto;

public static class HashAlgorithmFactory
{
    private static readonly ConcurrentDictionary<string, IHashAlgorithm> cache = new();

    public static IHashAlgorithm GetHash(IComponentContext ctx, JObject definition)
    {
        var hash = definition["hash"]?.Value<string>()?.ToLower();

        if(string.IsNullOrEmpty(hash))
            throw new NotSupportedException("$Invalid or empty hash value {hash}");

        var args = definition["args"]?
            .Select(token => token.Type == JTokenType.Object ? GetHash(ctx, (JObject) token) : token.Value<object>())
            .ToArray();

        return InstantiateHash(ctx, hash, args);
    }

    private static IHashAlgorithm InstantiateHash(IComponentContext ctx, string name, object[] args)
    {
        var hasArgs = args is { Length: > 0 };

        // check cache
        if(!hasArgs && cache.TryGetValue(name, out var result))
            return result;

        // instantiate (through DI)
        if(!hasArgs)
        {
            result = ctx.ResolveNamed<IHashAlgorithm>(name);
            cache.TryAdd(name, result);
        }

        else
            result = ctx.ResolveNamed<IHashAlgorithm>(name, args.Select((x, i) => new PositionalParameter(i, x)));

        return result;
    }
}
