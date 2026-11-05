using IndexNow.Configuration;
using IndexNow.Filtering;
using IndexNow.Queue;
using IndexNow.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Submission;

/// <summary>Turns a collected set of changes into submissions. Scoped: one instance per batch.</summary>
internal sealed class IndexNowBatchProcessor(
    IContentService contentService,
    IIndexNowUrlResolver urlResolver,
    IEnumerable<IIndexNowUrlFilter> filters,
    IIndexNowClient client,
    IOptions<IndexNowOptions> options,
    ILogger<IndexNowBatchProcessor> logger)
{
    public async Task ProcessAsync(IReadOnlyCollection<IndexNowWorkItem> items, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<Uri, IndexNowCandidate>();

        // Current URLs first: a URL that was captured before a publish and still exists is an update, not a removal.
        foreach (Guid key in items.Select(item => item.ContentKey).OfType<Guid>().Distinct())
        {
            IContent? content = contentService.GetById(key);
            if (content is null || content.Trashed)
            {
                continue;
            }

            foreach (ResolvedUrl url in urlResolver.Resolve(content))
            {
                AddCandidate(candidates, url.Url, IndexNowChangeType.Updated, content, url.Culture);
            }
        }

        foreach (IndexNowWorkItem item in items.Where(item => item.Url is not null))
        {
            AddCandidate(candidates, item.Url!, item.ChangeType, item.Content, item.Culture);
        }

        var accepted = new List<Uri>();
        foreach (IndexNowCandidate candidate in candidates.Values)
        {
            if (await PassesFiltersAsync(candidate, cancellationToken))
            {
                accepted.Add(candidate.Url);
            }
        }

        foreach (IndexNowBatch batch in IndexNowBatchBuilder.Build(accepted))
        {
            await client.SubmitAsync(batch, cancellationToken);
        }
    }

    private void AddCandidate(Dictionary<Uri, IndexNowCandidate> candidates, string url, IndexNowChangeType changeType, IContent? content, string? culture)
    {
        Uri? normalized = IndexNowUrlNormalizer.Normalize(url, options.Value);
        if (normalized is null)
        {
            logger.LogDebug("IndexNow skips {Url}: not a public URL, or excluded by the host or path settings", url);
            return;
        }

        candidates.TryAdd(normalized, new IndexNowCandidate(normalized, changeType, content, culture));
    }

    private async Task<bool> PassesFiltersAsync(IndexNowCandidate candidate, CancellationToken cancellationToken)
    {
        foreach (IIndexNowUrlFilter filter in filters)
        {
            try
            {
                if (!await filter.ShouldSubmitAsync(candidate, cancellationToken))
                {
                    logger.LogDebug("IndexNow skips {Url}: excluded by {Filter}", candidate.Url, filter.GetType().Name);
                    return false;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // When in doubt, leave it out: a page that should not be indexed must not be announced.
                logger.LogError(exception, "IndexNow filter {Filter} failed on {Url}, so the URL is skipped", filter.GetType().Name, candidate.Url);
                return false;
            }
        }

        return true;
    }
}
