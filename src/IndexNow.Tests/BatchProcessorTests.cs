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
            IndexNowWorkItem.ForRemovedUrl("https://www.one.nl/old-name/", Mock.Of<IContent>(), null),
            IndexNowWorkItem.ForContent(key));

        Assert.Equal(["https://www.one.nl/new-name/", "https://www.one.nl/old-name/"], Assert.Single(_submitted).Urls);
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
    {
        var key = Guid.NewGuid();
        var content = new Mock<IContent>();
        content.Setup(item => item.Key).Returns(key);
        content.Setup(item => item.Trashed).Returns(isTrashed);
        _contentService.Setup(service => service.GetById(key)).Returns(content.Object);
        _resolver.Setup(resolver => resolver.Resolve(content.Object)).Returns([new ResolvedUrl(url, null)]);
        return key;
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
