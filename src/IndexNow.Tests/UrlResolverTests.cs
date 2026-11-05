using IndexNow.Urls;
using Moq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace IndexNow.Tests;

public class UrlResolverTests
{
    [Fact]
    public void Never_builds_a_url_on_the_current_request()
    {
        var urlProvider = new Mock<IPublishedUrlProvider>();
        urlProvider
            .Setup(provider => provider.GetUrl(It.IsAny<Guid>(), UrlMode.Absolute, It.IsAny<string?>(), It.IsAny<Uri?>()))
            .Returns("https://www.one.nl/page/");

        IReadOnlyList<ResolvedUrl> urls = Resolver(urlProvider.Object).Resolve(Content());

        Assert.Equal("https://www.one.nl/page/", Assert.Single(urls).Url);
        urlProvider.Verify(provider => provider.GetUrl(It.IsAny<Guid>(), UrlMode.Absolute, null, It.Is<Uri?>(current => current != null && current.Scheme == "https")), Times.Once);
    }

    [Theory]
    [InlineData("#")]
    [InlineData("#ex")]
    [InlineData("")]
    public void Returns_nothing_without_a_published_url(string url)
    {
        var urlProvider = new Mock<IPublishedUrlProvider>();
        urlProvider
            .Setup(provider => provider.GetUrl(It.IsAny<Guid>(), UrlMode.Absolute, It.IsAny<string?>(), It.IsAny<Uri?>()))
            .Returns(url);

        Assert.Empty(Resolver(urlProvider.Object).Resolve(Content()));
    }

    [Fact]
    public void Reads_the_cultures_of_a_variant_page_from_what_is_live()
    {
        // Umbraco has emptied PublishedCultures by the time the notification that reads the disappearing URLs comes in.
        IContent content = Content(ContentVariation.Culture);
        var published = new Mock<IPublishedContent>();
        published.Setup(item => item.Cultures).Returns(new Dictionary<string, PublishedCultureInfo>
        {
            ["en-US"] = new("en-US", "Page", "page", DateTime.UtcNow),
            ["nl-NL"] = new("nl-NL", "Pagina", "pagina", DateTime.UtcNow),
        });
        var cache = new Mock<IPublishedContentCache>();
        cache.Setup(item => item.GetById(content.Key)).Returns(published.Object);
        var urlProvider = new Mock<IPublishedUrlProvider>();
        urlProvider.Setup(provider => provider.GetUrl(content.Key, UrlMode.Absolute, "en-US", It.IsAny<Uri?>())).Returns("https://www.one.nl/en/page/");
        urlProvider.Setup(provider => provider.GetUrl(content.Key, UrlMode.Absolute, "nl-NL", It.IsAny<Uri?>())).Returns("https://www.one.nl/nl/pagina/");

        IReadOnlyList<ResolvedUrl> urls = Resolver(urlProvider.Object, cache.Object).Resolve(content);

        Assert.Equal(["en-US", "nl-NL"], urls.Select(url => url.Culture));
    }

    [Fact]
    public void A_variant_page_that_is_not_live_has_no_urls()
        => Assert.Empty(Resolver(Mock.Of<IPublishedUrlProvider>(), Mock.Of<IPublishedContentCache>()).Resolve(Content(ContentVariation.Culture)));

    private static IndexNowUrlResolver Resolver(IPublishedUrlProvider urlProvider, IPublishedContentCache? contentCache = null)
    {
        var domains = new Mock<IDomainCache>();
        domains.Setup(cache => cache.GetAll(true)).Returns([]);
        var context = new Mock<IUmbracoContext>();
        context.Setup(item => item.Domains).Returns(domains.Object);
        context.Setup(item => item.Content).Returns(contentCache!);
        var factory = new Mock<IUmbracoContextFactory>();
        factory
            .Setup(item => item.EnsureUmbracoContext())
            .Returns(new UmbracoContextReference(context.Object, false, Mock.Of<IUmbracoContextAccessor>()));

        return new IndexNowUrlResolver(factory.Object, urlProvider);
    }

    private static IContent Content(ContentVariation variation = ContentVariation.Nothing)
    {
        var contentType = new Mock<ISimpleContentType>();
        contentType.Setup(type => type.Variations).Returns(variation);
        var content = new Mock<IContent>();
        content.Setup(item => item.ContentType).Returns(contentType.Object);
        content.Setup(item => item.Key).Returns(Guid.NewGuid());
        content.Setup(item => item.Path).Returns("-1,1000");
        return content.Object;
    }
}
