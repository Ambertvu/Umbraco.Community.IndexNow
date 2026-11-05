namespace IndexNow.Submission;

internal sealed record IndexNowBatch(string Host, IReadOnlyList<string> Urls);

internal static class IndexNowBatchBuilder
{
    /// <summary>The protocol's limit per request.</summary>
    public const int MaxUrlsPerRequest = 10_000;

    /// <summary>The protocol takes one host per request, so a multi-domain site gets a request per domain.</summary>
    public static IEnumerable<IndexNowBatch> Build(IEnumerable<Uri> urls, int maxUrlsPerRequest = MaxUrlsPerRequest)
        => urls
            .GroupBy(url => url.Host, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .Select(url => url.AbsoluteUri)
                .Distinct(StringComparer.Ordinal)
                .Chunk(maxUrlsPerRequest)
                .Select(chunk => new IndexNowBatch(group.Key.ToLowerInvariant(), chunk)));
}
