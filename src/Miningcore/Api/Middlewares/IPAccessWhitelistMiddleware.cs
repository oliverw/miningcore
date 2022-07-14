using Microsoft.AspNetCore.Http;
using NLog;
using System.Net;

namespace Miningcore.Api.Middlewares;

public class IPAccessWhitelistMiddleware
{
    public IPAccessWhitelistMiddleware(RequestDelegate next, string[] locations, IPAddress[] whitelist)
    {
        this.whitelist = whitelist;
        this.next = next;
        this.locations = locations;
    }

    private readonly RequestDelegate next;
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IPAddress[] whitelist;
    private readonly string[] locations;

    public async Task Invoke(HttpContext context)
    {
        if(locations.Any(x => context.Request.Path.Value.StartsWith(x)))
        {
            var remoteAddress = context.Connection.RemoteIpAddress;

            if(!whitelist.Any(x => x.Equals(remoteAddress)))
            {
                logger.Info(() => $"Unauthorized request attempt to {context.Request.Path.Value} from {remoteAddress}");

                context.Response.StatusCode = (int) HttpStatusCode.Forbidden;
                await context.Response.WriteAsync("You are not in my access list. Good Bye.\n");
                return;
            }
        }

        await next.Invoke(context);
    }
}
