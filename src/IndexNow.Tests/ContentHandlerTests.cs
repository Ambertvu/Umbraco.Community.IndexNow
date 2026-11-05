using IndexNow.Configuration;
using IndexNow.Notifications;
using IndexNow.Queue;
using IndexNow.Urls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Tests;

public class ContentHandlerTests
{
    private readonly EventMessages _messages = new();
    private readonly IndexNowQueue _queue;
    private readonly Mock<IIndexNowUrlResolver> _resolver = new();
    private readonly Mock<IContentService> _contentService = new();
    private readonly IndexNowContentHandler _handler;
    private int _nextId = 1000;

    public ContentHandlerTests()
    {
        IOptions<IndexNowOptions> options = Options.Create(new IndexNowOptions { Key = "abcd1234-key" });
        _queue = new IndexNowQueue(options);
        _handler = new IndexNowContentHandler(_queue, _resolver.Object, _contentService.Object, options, NullLogger<IndexNowContentHandler>.Instance);
    }

    [Fact]
    public void A_captured_url_only_enters_the_queue_together_with_the_change()
    {
        IContent page = Page("https://www.one.nl/old/");

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        Assert.Empty(Queued());

        _handler.Handle(new ContentPublishedNotification(page, _messages));
        List<IndexNowWorkItem> items = Queued();
        Assert.Equal("https://www.one.nl/old/", items[0].Url);
        Assert.Equal(page.Key, items[1].ContentKey);
    }

    [Fact]
    public void A_cancelled_change_sends_nothing()
    {
        IContent page = Page("https://www.one.nl/old/");

        _handler.Handle(new ContentUnpublishingNotification(page, _messages) { Cancel = true });
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Null(Assert.Single(Queued()).Url);
    }

    [Fact]
    public void A_change_that_never_got_saved_does_not_pile_up()
    {
        IContent page = Page("https://www.one.nl/old/");

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Single(Queued(), item => item.Url is not null);
    }

    [Fact]
    public void The_filters_get_the_stored_copy_because_umbraco_changes_the_entity()
    {
        IContent entity = Page("https://www.one.nl/old/");
        IContent stored = Mock.Of<IContent>();
        _contentService.Setup(service => service.GetById(entity.Id)).Returns(stored);

        _handler.Handle(new ContentMovingToRecycleBinNotification(new MoveToRecycleBinEventInfo<IContent>(entity, entity.Path), _messages));
        _handler.Handle(new ContentMovedToRecycleBinNotification(new MoveToRecycleBinEventInfo<IContent>(entity, entity.Path), _messages));

        Assert.Same(stored, Assert.Single(Queued()).Content);
    }

    [Fact]
    public void Unpublishing_captures_the_whole_branch()
    {
        IContent child = Page("https://www.one.nl/parent/child/");
        IContent parent = Page("https://www.one.nl/parent/");
        AddDescendants(parent, child);

        _handler.Handle(new ContentUnpublishingNotification(parent, _messages));
        _handler.Handle(new ContentUnpublishedNotification(parent, _messages));

        Assert.Equal(["https://www.one.nl/parent/", "https://www.one.nl/parent/child/"], Queued().Select(item => item.Url));
    }

    [Fact]
    public void A_move_captures_the_page_and_queues_the_change()
    {
        IContent page = Page("https://www.one.nl/old/");
        var move = new MoveEventInfo<IContent>(page, page.Path, -1, null);

        _handler.Handle(new ContentMovingNotification(move, _messages));
        _handler.Handle(new ContentMovedNotification(move, _messages));

        List<IndexNowWorkItem> items = Queued();
        Assert.Equal("https://www.one.nl/old/", items[0].Url);
        Assert.Equal(page.Key, items[1].ContentKey);
    }

    [Fact]
    public void Deleting_skips_what_was_in_the_recycle_bin_already()
    {
        IContent live = Page("https://www.one.nl/live/");
        IContent trashed = Page("https://www.one.nl/trashed/");
        Mock.Get(trashed).Setup(item => item.Trashed).Returns(true);

        IEnumerable<IContent> deleted = [live, trashed];
        _handler.Handle(new ContentDeletingNotification(deleted, _messages));
        _handler.Handle(new ContentDeletedNotification(live, _messages));
        _handler.Handle(new ContentDeletedNotification(trashed, _messages));

        Assert.Equal("https://www.one.nl/live/", Assert.Single(Queued()).Url);
    }

    [Fact]
    public void A_culture_that_goes_offline_takes_its_urls_below_the_page_along()
    {
        IContent child = Page(
            new ResolvedUrl("https://www.one.nl/en/page/child/", "en-US"),
            new ResolvedUrl("https://www.one.nl/nl/pagina/kind/", "nl-NL"));
        IContent parent = Page(new ResolvedUrl("https://www.one.nl/en/page/", "en-US"), new ResolvedUrl("https://www.one.nl/nl/pagina/", "nl-NL"));
        AddDescendants(parent, child);

        // What Umbraco's GetCulturesUnpublishing reads after UnpublishCulture("nl-NL").
        var cultures = new ContentCultureInfosCollection();
        cultures.AddOrUpdate("nl-NL", "Pagina", DateTime.UtcNow);
        var contentType = new Mock<ISimpleContentType>();
        contentType.Setup(type => type.Variations).Returns(ContentVariation.Culture);
        Mock<IContent> mock = Mock.Get(parent);
        mock.Setup(item => item.Published).Returns(true);
        mock.Setup(item => item.ContentType).Returns(contentType.Object);
        mock.Setup(item => item.CultureInfos).Returns(cultures);
        mock.Setup(item => item.IsPropertyDirty(It.IsAny<string>())).Returns(true);

        _handler.Handle(new ContentPublishingNotification(parent, _messages));
        _handler.Handle(new ContentPublishedNotification(parent, _messages));

        List<string?> urls = Queued().Select(item => item.Url).ToList();
        Assert.Contains("https://www.one.nl/nl/pagina/kind/", urls);
        Assert.DoesNotContain("https://www.one.nl/en/page/child/", urls);
    }

    [Fact]
    public void A_publish_queues_the_cultures_it_publishes()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        IndexNowWorkItem item = Assert.Single(Queued(), item => item.ContentKey == page.Key);
        Assert.Equal(["nl-NL"], item.Cultures!);
    }

    [Fact]
    public void Publishing_captures_every_live_culture_to_see_what_changed()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        _resolver.Setup(resolver => resolver.Resolve(page)).Returns(
        [
            new ResolvedUrl("https://www.one.nl/nl/", "nl-NL"),
            new ResolvedUrl("https://www.one.nl/en/", "en-US"),
        ]);

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Equal(["en-US", "nl-NL"], Queued().Where(item => item.Url is not null).Select(item => item.Culture).Order());
    }

    [Fact]
    public void A_publish_that_also_changes_an_invariant_value_is_about_every_culture()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        SetInvariantValue(page, "new");
        IContent stored = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        SetInvariantValue(stored, "old");
        _contentService.Setup(service => service.GetById(page.Id)).Returns(stored);

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Null(Assert.Single(Queued(), item => item.ContentKey == page.Key).Cultures);
    }

    [Fact]
    public void An_invariant_value_that_stays_the_same_keeps_the_publish_to_its_cultures()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        SetInvariantValue(page, "same");
        IContent stored = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        SetInvariantValue(stored, "same");
        _contentService.Setup(service => service.GetById(page.Id)).Returns(stored);

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Equal(["nl-NL"], Assert.Single(Queued(), item => item.ContentKey == page.Key).Cultures!);
    }

    [Fact]
    public void A_page_without_cultures_is_about_every_culture()
    {
        IContent page = Page("https://www.one.nl/page/");

        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Null(Assert.Single(Queued(), item => item.ContentKey == page.Key).Cultures);
    }

    [Fact]
    public void A_page_that_comes_back_with_its_parent_is_about_every_culture()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");

        // Umbraco sends no Publishing notification for these.
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Null(Assert.Single(Queued()).Cultures);
    }

    [Fact]
    public void Cultures_of_a_publish_that_got_cancelled_are_not_lost_or_in_the_way()
    {
        IContent page = VariantPage(publishing: "nl-NL", notPublishing: "en-US");
        _handler.Handle(new ContentPublishingNotification(page, _messages));

        Mock.Get(page).Setup(item => item.PublishCultureInfos).Returns(CultureInfos(dirty: "en-US", clean: "nl-NL"));
        _handler.Handle(new ContentPublishingNotification(page, _messages));
        _handler.Handle(new ContentPublishedNotification(page, _messages));

        Assert.Equal(2, Queued().Count(item => item.ContentKey == page.Key));
    }

    [Fact]
    public void A_domain_change_captures_the_site_and_queues_its_root()
    {
        IContent child = Page("https://www.one.nl/page/");
        IContent root = Page("https://www.one.nl/");
        AddDescendants(root, child);
        _contentService.Setup(service => service.GetById(root.Id)).Returns(root);
        var domain = new Mock<IDomain>();
        domain.Setup(item => item.RootContentId).Returns(root.Id);

        _handler.Handle(new DomainSavingNotification(domain.Object, _messages));
        Assert.Empty(Queued());

        _handler.Handle(new DomainSavedNotification(domain.Object, _messages));
        List<IndexNowWorkItem> items = Queued();
        Assert.Equal(["https://www.one.nl/", "https://www.one.nl/page/", null], items.Select(item => item.Url));
        Assert.Equal(root.Key, items[2].ContentKey);
    }

    private IContent Page(string url) => Page(new ResolvedUrl(url, null));

    private IContent Page(params ResolvedUrl[] urls)
    {
        var id = _nextId++;
        var contentType = new Mock<ISimpleContentType>();
        contentType.Setup(type => type.Variations).Returns(ContentVariation.Nothing);
        var content = new Mock<IContent>();
        content.Setup(item => item.Id).Returns(id);
        content.Setup(item => item.Key).Returns(Guid.NewGuid());
        content.Setup(item => item.Path).Returns($"-1,{id}");
        content.Setup(item => item.ContentType).Returns(contentType.Object);
        _resolver.Setup(resolver => resolver.Resolve(content.Object)).Returns(urls);
        return content.Object;
    }

    private IContent VariantPage(string publishing, string notPublishing)
    {
        IContent page = Page(Array.Empty<ResolvedUrl>());
        var contentType = new Mock<ISimpleContentType>();
        contentType.Setup(type => type.Variations).Returns(ContentVariation.Culture);
        Mock<IContent> mock = Mock.Get(page);
        mock.Setup(item => item.ContentType).Returns(contentType.Object);
        mock.Setup(item => item.Published).Returns(true);
        mock.Setup(item => item.PublishCultureInfos).Returns(CultureInfos(dirty: publishing, clean: notPublishing));
        return page;
    }

    private static ContentCultureInfosCollection CultureInfos(string dirty, string clean)
    {
        var cultures = new ContentCultureInfosCollection();
        cultures.AddOrUpdate(dirty, "Dirty", DateTime.UtcNow);
        cultures.AddOrUpdate(clean, "Clean", DateTime.UtcNow);
        cultures[clean].ResetDirtyProperties();
        return cultures;
    }

    private static void SetInvariantValue(IContent page, string value)
    {
        var propertyType = new Mock<IPropertyType>();
        propertyType.Setup(type => type.Variations).Returns(ContentVariation.Nothing);
        var property = new Mock<IProperty>();
        property.Setup(item => item.Alias).Returns("shared");
        property.Setup(item => item.PropertyType).Returns(propertyType.Object);
        property.Setup(item => item.GetValue(null, null, true)).Returns(value);

        IProperty? found = property.Object;
        var properties = new Mock<IPropertyCollection>();
        properties.Setup(item => item.GetEnumerator()).Returns(() => new List<IProperty> { property.Object }.GetEnumerator());
        properties.Setup(item => item.TryGetValue("shared", out found)).Returns(true);
        Mock.Get(page).Setup(item => item.Properties).Returns(properties.Object);
    }

    private void AddDescendants(IContent parent, params IContent[] descendants)
    {
        long total = descendants.Length;
        _contentService
            .Setup(service => service.GetPagedDescendants(parent.Id, 0, It.IsAny<int>(), out total, null, null))
            .Returns(descendants);
    }

    private List<IndexNowWorkItem> Queued()
    {
        var items = new List<IndexNowWorkItem>();
        while (_queue.Reader.TryRead(out IndexNowWorkItem? item))
        {
            items.Add(item);
        }

        return items;
    }
}
