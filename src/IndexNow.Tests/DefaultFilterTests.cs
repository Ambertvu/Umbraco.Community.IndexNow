using IndexNow.Configuration;
using IndexNow.Filtering;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Tests;

public class DefaultFilterTests
{
    private const int RootId = 1000;
    private const int PageId = 1001;

    private readonly Mock<IContentService> _contentService = new();

    [Fact]
    public async Task Allows_a_page_without_any_of_the_properties()
        => Assert.True(await ShouldSubmit(Page()));

    [Fact]
    public async Task Allows_a_url_without_content()
    {
        var candidate = new IndexNowCandidate(new Uri("https://www.example.com/"), IndexNowChangeType.Updated, null, null);
        Assert.True(await Filter().ShouldSubmitAsync(candidate, CancellationToken.None));
    }

    [Theory]
    [InlineData("noIndex")]
    [InlineData("NOINDEX")]
    [InlineData("hideInSeoSitemap")]
    [InlineData("hideInSEOSitemap")]
    [InlineData("doNotIndex")]
    public async Task Leaves_out_a_page_with_a_default_noindex_property_on(string alias)
        => Assert.False(await ShouldSubmit(Page((alias, true))));

    [Fact]
    public async Task Allows_a_page_with_the_noindex_property_off()
        => Assert.True(await ShouldSubmit(Page(("noIndex", false))));

    [Theory]
    [InlineData("index")]
    [InlineData("indexable")]
    [InlineData("allowIndexing")]
    public async Task Leaves_out_a_page_with_a_default_index_property_off(string alias)
        => Assert.False(await ShouldSubmit(Page((alias, false))));

    [Fact]
    public async Task Allows_a_page_with_the_index_property_on()
        => Assert.True(await ShouldSubmit(Page(("index", true))));

    [Fact]
    public async Task Does_not_treat_navigation_hiding_as_noindex()
        => Assert.True(await ShouldSubmit(Page(("umbracoNaviHide", true))));

    [Fact]
    public async Task Ignores_a_property_that_is_not_a_true_false()
    {
        // A text property called "index" has nothing to do with indexing.
        IContent page = Page(("index", false));
        Mock.Get(page.Properties["index"]!.PropertyType).Setup(type => type.PropertyEditorAlias).Returns(Constants.PropertyEditors.Aliases.TextBox);

        Assert.True(await ShouldSubmit(page));
    }

    [Fact]
    public async Task Adds_configured_properties_to_the_defaults()
    {
        var options = new IndexNowOptions { NoIndexProperties = ["hideInContent"], IndexProperties = ["showInSearch"] };

        Assert.False(await ShouldSubmit(Page(("hideInContent", true)), options));
        Assert.False(await ShouldSubmit(Page(("showInSearch", false)), options));
        Assert.False(await ShouldSubmit(Page(("noIndex", true)), options));
    }

    [Fact]
    public async Task Can_switch_the_defaults_off()
    {
        var options = new IndexNowOptions { UseDefaultProperties = false, NoIndexProperties = ["hideInContent"] };

        Assert.True(await ShouldSubmit(Page(("noIndex", true)), options));
        Assert.False(await ShouldSubmit(Page(("hideInContent", true)), options));
    }

    [Fact]
    public async Task Leaves_out_the_whole_site_when_the_root_is_noindex()
    {
        IContent root = Page(("noIndex", true));
        _contentService.Setup(service => service.GetById(RootId)).Returns(root);

        Assert.False(await ShouldSubmit(Page(path: $"-1,{RootId},{PageId}")));
    }

    [Fact]
    public async Task A_root_hidden_from_the_sitemap_does_not_hide_its_children()
    {
        IContent root = Page(("hideInSeoSitemap", true));
        _contentService.Setup(service => service.GetById(RootId)).Returns(root);

        Assert.True(await ShouldSubmit(Page(path: $"-1,{RootId},{PageId}")));
    }

    [Fact]
    public async Task Looks_up_the_root_once_per_batch()
    {
        _contentService.Setup(service => service.GetById(RootId)).Returns(Page());
        DefaultIndexNowUrlFilter filter = Filter();

        await filter.ShouldSubmitAsync(Candidate(Page(path: $"-1,{RootId},{PageId}")), CancellationToken.None);
        await filter.ShouldSubmitAsync(Candidate(Page(path: $"-1,{RootId},{PageId + 1}")), CancellationToken.None);

        _contentService.Verify(service => service.GetById(RootId), Times.Once);
    }

    [Fact]
    public async Task Leaves_out_a_page_without_a_template_unless_allowed()
    {
        IContent page = Page(templateId: null);

        Assert.False(await ShouldSubmit(page));
        Assert.True(await ShouldSubmit(page, new IndexNowOptions { RequireTemplate = false }));
    }

    [Fact]
    public async Task Leaves_out_excluded_document_types()
        => Assert.False(await ShouldSubmit(Page(), new IndexNowOptions { ExcludedDocumentTypes = ["TESTPAGE"] }));

    [Fact]
    public async Task Reads_a_culture_variant_property_for_the_culture_of_the_url()
    {
        IContent page = Page(("noIndex", false));
        Mock.Get(page.Properties["noIndex"]!.PropertyType).Setup(type => type.Variations).Returns(ContentVariation.Culture);
        Mock.Get(page).Setup(content => content.GetValue<bool>("noIndex", "nl-NL", null, true)).Returns(true);

        var dutch = new IndexNowCandidate(new Uri("https://www.example.com/"), IndexNowChangeType.Updated, page, "nl-NL");
        var english = new IndexNowCandidate(new Uri("https://www.example.com/en/"), IndexNowChangeType.Updated, page, "en-US");

        Assert.False(await Filter().ShouldSubmitAsync(dutch, CancellationToken.None));
        Assert.True(await Filter().ShouldSubmitAsync(english, CancellationToken.None));
    }

    private Task<bool> ShouldSubmit(IContent content, IndexNowOptions? options = null)
        => Filter(options).ShouldSubmitAsync(Candidate(content), CancellationToken.None).AsTask();

    private DefaultIndexNowUrlFilter Filter(IndexNowOptions? options = null)
        => new(_contentService.Object, Options.Create(options ?? new IndexNowOptions()));

    private static IndexNowCandidate Candidate(IContent content)
        => new(new Uri("https://www.example.com/page/"), IndexNowChangeType.Updated, content, null);

    private static IContent Page(params (string Alias, bool Value)[] properties)
        => Page($"-1,{PageId}", 1, properties);

    private static IContent Page(string path)
        => Page(path, 1);

    private static IContent Page(int? templateId)
        => Page($"-1,{PageId}", templateId);

    private static IContent Page(string path, int? templateId, params (string Alias, bool Value)[] properties)
    {
        var contentType = new Mock<ISimpleContentType>();
        contentType.Setup(type => type.Alias).Returns("testPage");

        var values = properties.ToDictionary(
            property => property.Alias,
            property => property.Value,
            StringComparer.OrdinalIgnoreCase);

        var collection = new Mock<IPropertyCollection>();
        foreach ((string alias, _) in properties)
        {
            var propertyType = new Mock<IPropertyType>();
            propertyType.Setup(type => type.PropertyEditorAlias).Returns(Constants.PropertyEditors.Aliases.Boolean);
            propertyType.Setup(type => type.Variations).Returns(ContentVariation.Nothing);

            var property = new Mock<IProperty>();
            property.Setup(item => item.PropertyType).Returns(propertyType.Object);
            IProperty? value = property.Object;

            collection.Setup(items => items[alias]).Returns(value);
        }

        collection
            .Setup(items => items.TryGetValue(It.IsAny<string>(), out It.Ref<IProperty?>.IsAny))
            .Returns(new TryGetProperty((string alias, out IProperty? property) =>
            {
                property = values.ContainsKey(alias) ? collection.Object[values.Keys.First(key => key.Equals(alias, StringComparison.OrdinalIgnoreCase))] : null;
                return property is not null;
            }));

        var content = new Mock<IContent>();
        content.Setup(item => item.ContentType).Returns(contentType.Object);
        content.Setup(item => item.Properties).Returns(collection.Object);
        content.Setup(item => item.TemplateId).Returns(templateId);
        content.Setup(item => item.Path).Returns(path);
        content.Setup(item => item.Id).Returns(int.Parse(path.Split(',')[^1]));
        content
            .Setup(item => item.GetValue<bool>(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), true))
            .Returns((string alias, string? _, string? _, bool _) => values[alias]);

        return content.Object;
    }

    private delegate bool TryGetProperty(string alias, out IProperty? property);
}
