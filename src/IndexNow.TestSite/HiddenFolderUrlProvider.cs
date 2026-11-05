using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;
#if UMBRACO_17
using DefaultProvider = Umbraco.Cms.Core.Routing.NewDefaultUrlProvider;
#else
using DefaultProvider = Umbraco.Cms.Core.Routing.DefaultUrlProvider;
#endif

namespace IndexNow.TestSite;

/// <summary>
/// Leaves the folder called <c>Hidden folder</c> out of URLs, the way sites hide a folder that only groups content. It only
/// builds URLs: nothing in the test site needs them to route.
/// </summary>
internal sealed class HiddenFolderUrlProvider(IServiceProvider services) : IUrlProvider
{
    private readonly DefaultProvider _inner = ActivatorUtilities.CreateInstance<DefaultProvider>(services);

    public string Alias => _inner.Alias;

    public UrlInfo? GetUrl(IPublishedContent content, UrlMode mode, string? culture, Uri current)
    {
        UrlInfo? url = _inner.GetUrl(content, mode, culture, current);
        return url?.Url is { } uri ? UrlInfo.AsUrl(uri.ToString().Replace("/hidden-folder/", "/"), Alias, culture) : url;
    }

    public IEnumerable<UrlInfo> GetOtherUrls(int id, Uri current) => _inner.GetOtherUrls(id, current);

    public Task<UrlInfo?> GetPreviewUrlAsync(IContent content, string? culture, string? segment)
        => _inner.GetPreviewUrlAsync(content, culture, segment);
}

public sealed class HiddenFolderComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.UrlProviders().Insert<HiddenFolderUrlProvider>();
}
