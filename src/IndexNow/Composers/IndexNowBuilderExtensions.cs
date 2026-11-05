using IndexNow.Filtering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;

namespace IndexNow.Composers;

public static class IndexNowBuilderExtensions
{
    /// <summary>Adds a filter of your own to the built-in rules. A URL is submitted only when every filter agrees.</summary>
    public static IUmbracoBuilder AddIndexNowUrlFilter<TFilter>(this IUmbracoBuilder builder)
        where TFilter : class, IIndexNowUrlFilter
    {
        builder.Services.AddScoped<IIndexNowUrlFilter, TFilter>();
        return builder;
    }
}
