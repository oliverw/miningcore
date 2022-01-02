using System.Reflection;
using Miningcore.Api.WebSocketNotifications;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace Miningcore.Api;

public class NSwagDocumentProcessor : IDocumentProcessor
{
    private static readonly string[] additionalNamespacesToInclude =
    {
        typeof(Notifications.Messages.AdminNotification).Namespace,
    };

    private static readonly Type[] additionalTypesToInclude =
    {
        typeof(WsNotificationType),
    };

    public void Process(DocumentProcessorContext context)
    {
        // collect types
        var types = GetType().Assembly.ExportedTypes.Where(t =>
        {
            if (!t.GetTypeInfo().IsClass && !t.GetTypeInfo().IsInterface && !t.GetTypeInfo().IsEnum)
                return false;

            foreach (var ns in additionalNamespacesToInclude)
            {
                if (t?.Namespace?.StartsWith(ns) == true)
                    return true;
            }

            foreach (var type in additionalTypesToInclude)
            {
                if (t == type)
                    return true;
            }

            return false;
        }).ToArray();

        // generate
        foreach (var type in types)
        {
            if (!context.SchemaResolver.HasSchema(type, false))
                context.SchemaGenerator.Generate(type, context.SchemaResolver);
        }
    }
}
