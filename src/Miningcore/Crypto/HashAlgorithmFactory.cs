using System.Collections.Concurrent;
using Autofac;
using Newtonsoft.Json.Linq;

namespace Miningcore.Crypto;

public static class HashAlgorithmFactory
{
    private static readonly ConcurrentDictionary<string, IHashAlgorithm> cache = new();

    public static IHashAlgorithm GetHash(IComponentContext ctx, JObject definition)
    {
        if(definition == null)
            return null;

        var hash = definition["hash"]?.Value<string>()?.ToLower();

        if(string.IsNullOrEmpty(hash))
            throw new NotSupportedException("$Invalid or empty hash value {hash}");

        var parameters = definition["args"]?
            .Select(token => token.Type == JTokenType.Object ? GetHash(ctx, (JObject) token) : token.Value<object>())
            .ToArray();

        return InstantiateHash(ctx, hash, parameters);
    }

    private static IHashAlgorithm InstantiateHash(IComponentContext ctx, string name, object[] parameters)
    {
        var isParameterized = parameters is { Length: > 0 };

        // check cache
        if(!isParameterized && cache.TryGetValue(name, out var result))
            return result;

        if(!isParameterized)
        {
            result = ctx.ResolveNamed<IHashAlgorithm>(name);

            cache.TryAdd(name, result);
        }

        else
        {
            var positionalParameters = parameters.Select((x, i) => new PositionalParameter(i, x));

            result = ctx.ResolveNamed<IHashAlgorithm>(name, positionalParameters);
        }

        return result;
    }
}
