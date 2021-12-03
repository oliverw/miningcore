using System.Collections.Concurrent;
using Autofac;
using Autofac.Core.Registration;
using Newtonsoft.Json.Linq;

namespace Miningcore.Crypto.Hashing.Equihash;

public static class EquihashSolverFactory
{
    private const string HashName = "equihash";
    private static readonly ConcurrentDictionary<string, EquihashSolver> cache = new();

    public static EquihashSolver GetSolver(IComponentContext ctx, JObject definition)
    {
        var hash = definition["hash"]?.Value<string>().ToLower();

        if(string.IsNullOrEmpty(hash) || hash != HashName)
            throw new NotSupportedException($"Invalid hash value '{hash}'. Expected '{HashName}'");

        var args = definition["args"]?
            .Select(token => token.Value<object>())
            .ToArray();

        if(args?.Length != 3)
            throw new NotSupportedException($"Invalid hash arguments '{string.Join(", ", args)}'");

        return InstantiateSolver(ctx, args);
    }

    private static EquihashSolver InstantiateSolver(IComponentContext ctx, object[] args)
    {
        var key = string.Join("-", args);
        if(cache.TryGetValue(key, out var result))
            return result;

        var n = (int) Convert.ChangeType(args[0], typeof(int));
        var k = (int) Convert.ChangeType(args[1], typeof(int));
        var personalization = args[2].ToString();

        // Lookup type
        var hashClass = (typeof(EquihashSolver).Namespace + $".EquihashSolver_{n}_{k}");
        var hashType = typeof(EquihashSolver).Assembly.GetType(hashClass, true);

        try
        {
            // create it (we'll let Autofac do the heavy lifting)
            result = (EquihashSolver) ctx.Resolve(hashType, new PositionalParameter(0, personalization));
        }

        catch(ComponentNotRegisteredException)
        {
            throw new NotSupportedException($"Equihash variant {n}_{k} is currently not implemented");
        }

        cache.TryAdd(key, result);
        return result;
    }
}
