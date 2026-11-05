using IndexNow.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IndexNow.KeyFile;

/// <summary>
/// Answers <c>/{key}.txt</c> on every domain, which is how search engines verify that the key belongs to a host. Served
/// from configuration because a container's disk does not survive a deploy.
/// </summary>
internal sealed class IndexNowKeyFileMiddleware(RequestDelegate next, IOptions<IndexNowOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        IndexNowOptions settings = options.Value;
        if (settings.IsActive
            && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && context.Request.Path.Equals($"/{settings.Key}.txt", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/plain; charset=utf-8";
            if (HttpMethods.IsGet(context.Request.Method))
            {
                await context.Response.WriteAsync(settings.Key!, context.RequestAborted);
            }

            return;
        }

        await next(context);
    }
}
