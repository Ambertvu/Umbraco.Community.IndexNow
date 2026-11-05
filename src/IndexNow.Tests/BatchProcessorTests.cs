using IndexNow.Configuration;
using IndexNow.Filtering;
using IndexNow.Queue;
using IndexNow.Submission;
using IndexNow.Urls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Tests;

public class BatchProcessorTests
{
    private readonly Mock<IContentService> _contentService = new();
    private readonly Mock<IIndexNowUrlResolver> _resolver = new();
    private readonly List<IndexNowBatch> _submitted = [];
    private readonly List<IIndexNowUrlFilter> _filters = [];
    private readonly Dictionary<Guid, IContent> _content = [];

    [Fact]
    public async Task Submits_the_current_urls_of_changed_content()
    {
        Guid key = AddContent("https://www.one.nl/page/");

        await Process(IndexNowWorkItem.ForContent(key));

        Assert.Equal(["https://www.one.nl/page/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task Skips_content_that_is_gone_or_trashed()
    {
        Guid trashed = AddContent("https://www.one.nl/trashed/", isTrashed: true);

        await Process(IndexNowWorkItem.ForContent(trashed), IndexNowWorkItem.ForContent(Guid.NewGuid()));

        Assert.Empty(_submitted);
    }

    [Fact]
    public async Task Submits_both_the_old_and_the_new_url_of_a_rename()
    {
        Guid key = AddContent("https://www.one.nl/new-name/");

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old-name/", _content[key], null),
            IndexNowWorkItem.ForContent(key));

        Assert.Equal(["https://www.one.nl/new-name/", "https://www.one.nl/old-name/"], Assert.Single(_submitted).Urls);
    }

    [Theory]
    [InlineData("https://www.one.nl/new", "https://www.one.nl/old", "https://www.one.nl/new/child", "https://www.one.nl/old/child")]
    [InlineData("https://www.one.nl/new/", "https://www.one.nl/old/", "https://www.one.nl/new/child/", "https://www.one.nl/old/child/")]
    public async Task A_rename_also_submits_the_old_and_new_urls_below_the_page(string newUrl, string oldUrl, string newChildUrl, string oldChildUrl)
    {
        Guid parent = AddContent(newUrl);
        Guid child = AddContent(newChildUrl);
        AddDescendants(parent, child);

        await Process(IndexNowWorkItem.ForRemovedUrl(oldUrl, _content[parent], null), IndexNowWorkItem.ForContent(parent));

        Assert.Equal(new[] { newUrl, oldUrl, newChildUrl, oldChildUrl }.Order(), Assert.Single(_submitted).Urls.Order());
    }

    [Fact]
    public async Task A_rename_handles_every_level_below_the_page()
    {
        Guid parent = AddContent("https://www.one.nl/new/");
        Guid child = AddContent("https://www.one.nl/new/child/");
        Guid grandchild = AddContent("https://www.one.nl/new/child/grandchild/");
        AddDescendants(parent, child, grandchild);

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/", _content[parent], null), IndexNowWorkItem.ForContent(parent));

        Assert.Contains("https://www.one.nl/old/child/grandchild/", Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_publish_that_changes_no_url_does_not_look_up_the_branch()
    {
        Guid key = AddContent("https://www.one.nl/page/");
        Guid child = AddContent("https://www.one.nl/page/child/");
        AddDescendants(key, child);

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/page/", _content[key], null), IndexNowWorkItem.ForContent(key));

        Assert.Equal(["https://www.one.nl/page/"], Assert.Single(_submitted).Urls);
        long total;
        _contentService.Verify(service => service.GetPagedDescendants(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), out total, null, null), Times.Never);
    }

    [Fact]
    public async Task A_rename_passes_the_pages_below_it_through_the_filters()
    {
        Guid parent = AddContent("https://www.one.nl/new/");
        Guid hidden = AddContent("https://www.one.nl/new/hidden/");
        AddDescendants(parent, hidden);
        _filters.Add(new DelegateFilter(candidate => candidate.Content?.Key != hidden));

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/", _content[parent], null), IndexNowWorkItem.ForContent(parent));

        Assert.Equal(["https://www.one.nl/new/", "https://www.one.nl/old/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_rename_skips_pages_below_it_that_are_not_live()
    {
        Guid parent = AddContent("https://www.one.nl/new/");
        Guid trashed = AddContent("https://www.one.nl/new/trashed/", isTrashed: true);
        AddDescendants(parent, trashed);

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/", _content[parent], null), IndexNowWorkItem.ForContent(parent));

        Assert.Equal(["https://www.one.nl/new/", "https://www.one.nl/old/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_rename_in_one_culture_only_affects_that_culture_below_the_page()
    {
        Guid parent = AddContent(
            new ResolvedUrl("https://www.one.nl/nieuw/", "nl-NL"),
            new ResolvedUrl("https://www.one.nl/en/same/", "en-US"));
        Guid child = AddContent(
            new ResolvedUrl("https://www.one.nl/nieuw/kind/", "nl-NL"),
            new ResolvedUrl("https://www.one.nl/en/same/child/", "en-US"));
        AddDescendants(parent, child);

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/oud/", _content[parent], "nl-NL"),
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/en/same/", _content[parent], "en-US"),
            IndexNowWorkItem.ForContent(parent));

        var urls = Assert.Single(_submitted).Urls;
        Assert.Contains("https://www.one.nl/oud/kind/", urls);
        Assert.DoesNotContain(urls, url => url.Contains("/en/same/child/") && url != "https://www.one.nl/en/same/child/");
        Assert.Contains("https://www.one.nl/en/same/child/", urls);
    }

    [Fact]
    public async Task A_branch_is_walked_once_when_pages_below_a_changed_page_changed_too()
    {
        Guid parent = AddContent("https://www.one.nl/new/");
        Guid child = AddContent("https://www.one.nl/new/child/");
        AddDescendants(parent, child);

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/", _content[parent], null),
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/child/", _content[child], null),
            IndexNowWorkItem.ForContent(child),
            IndexNowWorkItem.ForContent(parent));

        long total;
        _contentService.Verify(service => service.GetPagedDescendants(_content[parent].Id, 0, It.IsAny<int>(), out total, null, null), Times.Once);
        _contentService.Verify(service => service.GetPagedDescendants(_content[child].Id, It.IsAny<long>(), It.IsAny<int>(), out total, null, null), Times.Never);
        Assert.Equal(4, Assert.Single(_submitted).Urls.Count);
    }

    [Fact]
    public async Task A_timeout_in_a_filter_only_skips_that_url()
    {
        _filters.Add(new DelegateFilter(candidate => candidate.Url.AbsolutePath.Contains("slow") ? throw new TaskCanceledException() : true));

        await Process(
            IndexNowWorkItem.ForUrl("https://www.one.nl/slow/"),
            IndexNowWorkItem.ForUrl("https://www.one.nl/fine/"));

        Assert.Equal(["https://www.one.nl/fine/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_url_that_still_exists_counts_as_an_update()
    {
        Guid key = AddContent("https://www.one.nl/page/");
        var seen = new List<IndexNowCandidate>();
        _filters.Add(new DelegateFilter(candidate =>
        {
            seen.Add(candidate);
            return true;
        }));

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/page/", Mock.Of<IContent>(), null),
            IndexNowWorkItem.ForContent(key));

        Assert.Equal(IndexNowChangeType.Updated, Assert.Single(seen).ChangeType);
    }

    [Fact]
    public async Task A_page_that_stops_being_indexable_is_still_submitted()
    {
        Guid key = AddContent("https://www.one.nl/page/");
        _filters.Add(new DelegateFilter(candidate => candidate.ChangeType == IndexNowChangeType.Removed));

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/page/", _content[key], null),
            IndexNowWorkItem.ForContent(key));

        Assert.Equal(["https://www.one.nl/page/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_page_that_was_and_is_excluded_is_not_submitted()
    {
        Guid key = AddContent("https://www.one.nl/page/");
        _filters.Add(new DelegateFilter(_ => false));

        await Process(
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/page/", _content[key], null),
            IndexNowWorkItem.ForContent(key));

        Assert.Empty(_submitted);
    }

    [Fact]
    public async Task A_url_shared_with_a_hidden_folder_is_submitted_for_the_page_behind_it()
    {
        Guid folder = AddContent("https://www.one.nl/parent/");
        Guid parent = AddContent("https://www.one.nl/parent/");
        _filters.Add(new DelegateFilter(candidate => candidate.Content?.Key != folder));

        await Process(IndexNowWorkItem.ForContent(folder), IndexNowWorkItem.ForContent(parent));

        Assert.Equal(["https://www.one.nl/parent/"], Assert.Single(_submitted).Urls);
    }

    [Theory]
    [InlineData(null, "nl-NL")]
    [InlineData("nl-NL", null)]
    public async Task A_rename_reaches_pages_below_it_that_vary_differently(string? parentCulture, string? childCulture)
    {
        Guid parent = AddContent(new ResolvedUrl("https://www.one.nl/new/", parentCulture));
        Guid child = AddContent(new ResolvedUrl("https://www.one.nl/new/child/", childCulture));
        AddDescendants(parent, child);

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old/", _content[parent], parentCulture), IndexNowWorkItem.ForContent(parent));

        Assert.Contains("https://www.one.nl/old/child/", Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_culture_that_goes_live_also_submits_the_pages_below_it()
    {
        Guid parent = AddContent(new ResolvedUrl("https://www.one.nl/nl/", "nl-NL"), new ResolvedUrl("https://www.one.nl/en/", "en-US"));
        Guid child = AddContent(new ResolvedUrl("https://www.one.nl/nl/child/", "nl-NL"), new ResolvedUrl("https://www.one.nl/en/child/", "en-US"));
        AddDescendants(parent, child);

        await Process(IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/nl/", _content[parent], "nl-NL"), IndexNowWorkItem.ForContent(parent));

        Assert.Contains("https://www.one.nl/en/child/", Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task A_page_that_goes_live_for_the_first_time_does_not_look_up_the_branch()
    {
        Guid key = AddContent("https://www.one.nl/page/");

        await Process(IndexNowWorkItem.ForContent(key));

        long total;
        _contentService.Verify(service => service.GetPagedDescendants(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), out total, null, null), Times.Never);
    }

    [Fact]
    public async Task Sends_one_request_per_host()
    {
        Guid one = AddContent("https://www.one.nl/page/");
        Guid two = AddContent("https://www.two.nl/page/");

        await Process(IndexNowWorkItem.ForContent(one), IndexNowWorkItem.ForContent(two));

        Assert.Equal(["www.one.nl", "www.two.nl"], _submitted.Select(batch => batch.Host));
    }

    [Fact]
    public async Task Leaves_out_what_a_filter_rejects()
    {
        _filters.Add(new DelegateFilter(candidate => !candidate.Url.AbsolutePath.Contains("secret")));

        await Process(
            IndexNowWorkItem.ForUrl("https://www.one.nl/public/"),
            IndexNowWorkItem.ForUrl("https://www.one.nl/secret/"));

        Assert.Equal(["https://www.one.nl/public/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task Leaves_out_a_url_when_a_filter_throws_and_carries_on()
    {
        _filters.Add(new DelegateFilter(candidate => candidate.Url.AbsolutePath.Contains("broken") ? throw new InvalidOperationException() : true));

        await Process(
            IndexNowWorkItem.ForUrl("https://www.one.nl/broken/"),
            IndexNowWorkItem.ForUrl("https://www.one.nl/fine/"));

        Assert.Equal(["https://www.one.nl/fine/"], Assert.Single(_submitted).Urls);
    }

    [Fact]
    public async Task Submits_nothing_for_local_urls()
    {
        Guid key = AddContent("http://localhost/page/");

        await Process(IndexNowWorkItem.ForContent(key));

        Assert.Empty(_submitted);
    }

    [Fact]
    public async Task Sends_no_request_for_an_empty_result()
    {
        await Process();
        Assert.Empty(_submitted);
    }

    private Guid AddContent(string url, bool isTrashed = false)
        => AddContent(isTrashed, [new ResolvedUrl(url, null)]);

    private Guid AddContent(params ResolvedUrl[] urls)
        => AddContent(false, urls);

    private Guid AddContent(bool isTrashed, ResolvedUrl[] urls)
    {
        var key = Guid.NewGuid();
        var content = new Mock<IContent>();
        content.Setup(item => item.Key).Returns(key);
        var id = 1000 + _content.Count;
        content.Setup(item => item.Id).Returns(id);
        content.Setup(item => item.Path).Returns($"-1,{id}");
        content.Setup(item => item.Trashed).Returns(isTrashed);
        _content[key] = content.Object;
        _contentService.Setup(service => service.GetById(key)).Returns(content.Object);
        _resolver.Setup(resolver => resolver.Resolve(content.Object)).Returns(urls);
        return key;
    }

    private void AddDescendants(Guid parent, params Guid[] descendants)
    {
        IContent[] page = descendants.Select(key => _content[key]).ToArray();
        foreach (IContent descendant in page)
        {
            Mock.Get(descendant).Setup(item => item.Path).Returns($"-1,{_content[parent].Id},{descendant.Id}");
        }

        long total = page.Length;
        _contentService
            .Setup(service => service.GetPagedDescendants(_content[parent].Id, 0, It.IsAny<int>(), out total, null, null))
            .Returns(page);
    }

    private Task Process(params IndexNowWorkItem[] items)
    {
        var client = new Mock<IIndexNowClient>();
        client
            .Setup(item => item.SubmitAsync(It.IsAny<IndexNowBatch>(), It.IsAny<CancellationToken>()))
            .Callback((IndexNowBatch batch, CancellationToken _) => _submitted.Add(batch))
            .ReturnsAsync(true);

        var processor = new IndexNowBatchProcessor(
            _contentService.Object,
            _resolver.Object,
            _filters,
            client.Object,
            Options.Create(new IndexNowOptions { Key = "abcd1234-key" }),
            NullLogger<IndexNowBatchProcessor>.Instance);

        return processor.ProcessAsync(items, CancellationToken.None);
    }

    private sealed class DelegateFilter(Func<IndexNowCandidate, bool> predicate) : IIndexNowUrlFilter
    {
        public ValueTask<bool> ShouldSubmitAsync(IndexNowCandidate candidate, CancellationToken cancellationToken)
            => ValueTask.FromResult(predicate(candidate));
    }
}
