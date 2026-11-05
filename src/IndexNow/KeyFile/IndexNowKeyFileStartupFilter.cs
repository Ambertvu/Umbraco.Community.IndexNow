using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace IndexNow.KeyFile;

/// <summary>Puts the key file in front of the whole pipeline, so that redirect rules and maintenance pages cannot break verification.</summary>
internal sealed class IndexNowKeyFileStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<IndexNowKeyFileMiddleware>();
            next(app);
        };
}
