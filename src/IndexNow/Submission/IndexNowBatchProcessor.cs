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
        var candidates = new Dictionary<Uri, List<IndexNowCandidate>>();
        var previousUrls = items.Where(item => item.Url is not null).ToList();
        var previousUrlsByContent = previousUrls.Where(item => item.Content is not null).ToLookup(item => item.Content!.Key);
        var changedContent = new List<IContent>();
        var changesByContentId = new Dictionary<int, BranchChange>();
        var untouchedUrls = new HashSet<(Guid ContentKey, string Url)>();

        // Current URLs first: a URL that was captured before a publish and still exists is first of all an update.
        foreach (IGrouping<Guid, IndexNowWorkItem> group in items.Where(item => item.ContentKey is not null).GroupBy(item => item.ContentKey!.Value))
        {
            Guid key = group.Key;
            IContent? content = contentService.GetById(key);
            if (content is null || content.Trashed)
            {
                continue;
            }

            IReadOnlyList<ResolvedUrl> urls = urlResolver.Resolve(content);
            List<IndexNowWorkItem> previous = previousUrlsByContent[key].ToList();
            var branchChange = new BranchChange(GetUrlChanges(urls, previous), GetNewCultures(urls, previous));
            IReadOnlySet<string>? changedCultures = GetChangedCultures(group);

            foreach (ResolvedUrl url in urls)
            {
                if (url.Culture is null || changedCultures is null || changedCultures.Contains(url.Culture) || branchChange.AppliesTo(url.Culture))
                {
                    AddCandidate(candidates, url.Url, IndexNowChangeType.Updated, content, url.Culture);
                }
                else
                {
                    untouchedUrls.Add((key, url.Url));
                }
            }

            if (branchChange.HasChanges)
            {
                changedContent.Add(content);
                changesByContentId[content.Id] = branchChange;
            }
        }

        var previousUrlsOfDescendants = new List<IndexNowWorkItem>();

        // Walk a branch once: a changed page below another changed page is part of that page's walk.
        foreach (IContent content in changedContent.Where(content => !AncestorIds(content).Any(changesByContentId.ContainsKey)))
        {
            AddDescendants(content, changesByContentId, candidates, previousUrlsOfDescendants);
        }

        // Everything that was live is captured before a publish, to see what changed. A URL of a culture that was left alone stays out.
        foreach (IndexNowWorkItem item in previousUrls
            .Where(item => item.Content is null || !untouchedUrls.Contains((item.Content.Key, item.Url!)))
            .Concat(previousUrlsOfDescendants))
        {
            AddCandidate(candidates, item.Url!, item.ChangeType, item.Content, item.Culture);
        }

        var accepted = new List<Uri>();
        foreach ((Uri url, List<IndexNowCandidate> urlCandidates) in candidates)
        {
            // Pages can share a URL, such as a folder that is hidden from the URL and its parent. And a page that was indexable
            // before this change but no longer is must still be submitted, or search engines never learn that it changed.
            foreach (IndexNowCandidate candidate in urlCandidates)
            {
                if (await PassesFiltersAsync(candidate, cancellationToken))
                {
                    accepted.Add(url);
                    break;
                }
            }
        }

        foreach (IndexNowBatch batch in IndexNowBatchBuilder.Build(accepted))
        {
            await client.SubmitAsync(batch, cancellationToken);
        }
    }

    /// <summary>The URLs of a content item that are different from what was captured before the change, per culture.</summary>
    private static List<UrlChange> GetUrlChanges(IReadOnlyList<ResolvedUrl> currentUrls, IEnumerable<IndexNowWorkItem> previousUrls)
    {
        var changes = new List<UrlChange>();
        foreach (IndexNowWorkItem previous in previousUrls)
        {
            foreach (ResolvedUrl current in currentUrls)
            {
                if (string.Equals(current.Culture, previous.Culture, StringComparison.OrdinalIgnoreCase) && current.Url != previous.Url)
                {
                    changes.Add(new UrlChange(previous.Url!, current.Url, current.Culture));
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// A culture that goes live on a page that was live already changes the URLs below it in that culture. Umbraco reports
    /// the pages below a page that goes live as a whole by itself.
    /// </summary>
    private static List<string?> GetNewCultures(IReadOnlyList<ResolvedUrl> currentUrls, List<IndexNowWorkItem> previousUrls)
        => previousUrls.Count == 0
            ? []
            : currentUrls
                .Where(current => !previousUrls.Any(previous => string.Equals(current.Culture, previous.Culture, StringComparison.OrdinalIgnoreCase)))
                .Select(current => current.Culture)
                .ToList();

    /// <returns>The cultures that changed in this batch; <c>null</c> when a change was about every culture.</returns>
    private static IReadOnlySet<string>? GetChangedCultures(IEnumerable<IndexNowWorkItem> items)
    {
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IndexNowWorkItem item in items)
        {
            if (item.Cultures is null)
            {
                return null;
            }

            cultures.UnionWith(item.Cultures);
        }

        return cultures;
    }

    /// <summary>
    /// A rename or move also changes the URLs of everything below the page. Those old URLs were not captured, because that
    /// would cost a lookup of the whole branch on every publish. They follow from the new URL, so this only runs when a URL
    /// did change.
    /// </summary>
    private void AddDescendants(IContent content, Dictionary<int, BranchChange> changesByContentId, Dictionary<Uri, List<IndexNowCandidate>> candidates, List<IndexNowWorkItem> previousUrlsOfDescendants)
    {
        foreach (IContent descendant in contentService.GetDescendants(content.Id))
        {
            if (descendant.Trashed)
            {
                continue;
            }

            List<BranchChange> branchChanges = AncestorIds(descendant)
                .Select(id => changesByContentId.GetValueOrDefault(id))
                .OfType<BranchChange>()
                .ToList();

            foreach (ResolvedUrl url in urlResolver.Resolve(descendant))
            {
                if (!branchChanges.Any(change => change.AppliesTo(url.Culture)))
                {
                    continue;
                }

                AddCandidate(candidates, url.Url, IndexNowChangeType.Updated, descendant, url.Culture);

                foreach (UrlChange change in branchChanges.SelectMany(change => change.UrlChanges).Where(change => change.AppliesTo(url.Culture)))
                {
                    if (change.TryGetPreviousUrlOf(url.Url) is { } previousUrl)
                    {
                        previousUrlsOfDescendants.Add(IndexNowWorkItem.ForRemovedUrl(previousUrl, descendant, url.Culture));
                    }
                }
            }
        }
    }

    private static IEnumerable<int> AncestorIds(IContent content)
        => content.Path.Split(',')
            .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
            .Where(id => id > 0 && id != content.Id);

    private void AddCandidate(Dictionary<Uri, List<IndexNowCandidate>> candidates, string url, IndexNowChangeType changeType, IContent? content, string? culture)
    {
        Uri? normalized = IndexNowUrlNormalizer.Normalize(url, options.Value);
        if (normalized is null)
        {
            logger.LogDebug("IndexNow skips {Url}: not a public URL, or excluded by the host or path settings", url);
            return;
        }

        if (!candidates.TryGetValue(normalized, out List<IndexNowCandidate>? urlCandidates))
        {
            urlCandidates = [];
            candidates[normalized] = urlCandidates;
        }

        if (!urlCandidates.Any(candidate => candidate.ChangeType == changeType && candidate.Content?.Key == content?.Key))
        {
            urlCandidates.Add(new IndexNowCandidate(normalized, changeType, content, culture));
        }
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
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // When in doubt, leave it out: a page that should not be indexed must not be announced.
                logger.LogError(exception, "IndexNow filter {Filter} failed on {Url}, so the URL is skipped", filter.GetType().Name, candidate.Url);
                return false;
            }
        }

        return true;
    }

    /// <summary>What a change to a page means for the URLs at and below it.</summary>
    private sealed record BranchChange(List<UrlChange> UrlChanges, List<string?> NewCultures)
    {
        public bool HasChanges => UrlChanges.Count > 0 || NewCultures.Count > 0;

        public bool AppliesTo(string? culture)
            => UrlChanges.Any(change => change.AppliesTo(culture))
                || (NewCultures.Count > 0 && (culture is null || NewCultures.Contains(null) || NewCultures.Contains(culture, StringComparer.OrdinalIgnoreCase)));
    }

    private sealed record UrlChange(string PreviousUrl, string CurrentUrl, string? Culture)
    {
        // A page without a culture can have pages with one below it and the other way around. The URL decides those.
        public bool AppliesTo(string? culture)
            => Culture is null || culture is null || string.Equals(Culture, culture, StringComparison.OrdinalIgnoreCase);

        /// <returns>What a URL below this page was before the change; <c>null</c> when it is not below this page.</returns>
        public string? TryGetPreviousUrlOf(string descendantUrl)
        {
            var currentBase = CurrentUrl.TrimEnd('/') + "/";
            return descendantUrl.StartsWith(currentBase, StringComparison.OrdinalIgnoreCase)
                ? PreviousUrl.TrimEnd('/') + "/" + descendantUrl[currentBase.Length..]
                : null;
        }
    }
}
