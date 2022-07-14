using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;

namespace Miningcore.Api.Middlewares;

/// <summary>
/// Publishes telemetry data of API request execution times
/// </summary>
public class ApiRequestMetricsMiddleware
{
    public ApiRequestMetricsMiddleware(RequestDelegate next, IMessageBus messageBus)
    {
        this.next = next;
        this.messageBus = messageBus;
    }

    private readonly RequestDelegate next;
    private readonly IMessageBus messageBus;

    public async Task Invoke(HttpContext context)
    {
        if(context.Request?.Path.StartsWithSegments("/api") == true)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                await next.Invoke(context);

                messageBus.SendTelemetry(context.Request.Path, TelemetryCategory.ApiRequest, null, sw.Elapsed, true);
            }

            catch
            {
                messageBus.SendTelemetry(context.Request.Path, TelemetryCategory.ApiRequest, null, sw.Elapsed, false);
                throw;
            }
        }

        else
            await next.Invoke(context);
    }
}
