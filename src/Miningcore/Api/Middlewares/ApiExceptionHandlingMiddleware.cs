using Microsoft.AspNetCore.Http;

namespace Miningcore.Api.Middlewares;

public class ApiExceptionHandlingMiddleware
{
    private readonly RequestDelegate next;

    public ApiExceptionHandlingMiddleware(RequestDelegate next)
    {
        this.next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }

        catch(ApiException ex)
        {
            await HandleResponseOverrideExceptionAsync(context, ex);
        }
    }

    private static async Task HandleResponseOverrideExceptionAsync(HttpContext context, ApiException ex)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        if(ex.ResponseStatusCode.HasValue)
            response.StatusCode = ex.ResponseStatusCode.Value;

        await response.WriteAsync(ex.Message).ConfigureAwait(false);
    }
}
