using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace IndexNow.Urls;

/// <summary>
/// Follows the culture lookup of Umbraco's own <c>PublishedUrlInfoProvider</c>, without its "other URLs" (those are the
/// non-canonical domains of a node) and collision routing (too slow for a branch, and a colliding URL is still live).
/// </summary>
internal sealed class IndexNowUrlResolver(IUmbracoContextFactory umbracoContextFactory, IPublishedUrlProvider urlProvider)
    : IIndexNowUrlResolver
{
    // Without this Umbraco builds on the current request: its scheme for a domain without one, its host to choose between
    // domains or for a site without any. A URL must not depend on who publishes from where, or on whether it is read in a
    // request or in the background, because URLs from both are compared to detect a rename.
    private static readonly Uri _current = new("https://localhost/");

    public IReadOnlyList<ResolvedUrl> Resolve(IContent content)
    {
        using UmbracoContextReference context = umbracoContextFactory.EnsureUmbracoContext();

        var urls = new List<ResolvedUrl>();
        foreach (var culture in GetCultures(content, context.UmbracoContext))
        {
            var url = urlProvider.GetUrl(content.Key, UrlMode.Absolute, culture, _current);
            if (!string.IsNullOrEmpty(url) && url is not "#" and not "#ex")
            {
                urls.Add(new ResolvedUrl(url, culture));
            }
        }

        return urls;
    }

    private static IEnumerable<string?> GetCultures(IContent content, IUmbracoContext umbracoContext)
    {
        if (content.ContentType.VariesByCulture())
        {
            // What is live, from the cache: Umbraco clears PublishedCultures before the notifications that read the URLs that
            // are about to disappear.
            return umbracoContext.Content.GetById(content.Key)?.Cultures.Keys ?? [];
        }

        var ancestorOrSelfIds = content.Path.Split(',').Select(id => int.TryParse(id, out var parsed) ? parsed : 0).ToHashSet();
        var domainCultures = umbracoContext.Domains?.GetAll(true)
            .Where(domain => ancestorOrSelfIds.Contains(domain.ContentId))
            .Select(domain => domain.Culture)
            .WhereNotNull()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return domainCultures is { Count: > 0 } ? domainCultures : new string?[] { null };
    }
}
