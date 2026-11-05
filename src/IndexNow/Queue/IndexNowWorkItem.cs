using IndexNow.Filtering;
using Umbraco.Cms.Core.Models;

namespace IndexNow.Queue;

/// <summary>Either a content key whose URLs are looked up at submit time, or a URL known already.</summary>
internal sealed record IndexNowWorkItem
{
    public Guid? ContentKey { get; private init; }

    /// <summary>The cultures of <see cref="ContentKey"/> that changed; <c>null</c> when that is every culture that is live.</summary>
    public IReadOnlySet<string>? Cultures { get; private init; }

    public string? Url { get; private init; }

    public IndexNowChangeType ChangeType { get; private init; }

    public IContent? Content { get; private init; }

    public string? Culture { get; private init; }

    public static IndexNowWorkItem ForContent(Guid contentKey, IReadOnlySet<string>? cultures = null)
        => new() { ContentKey = contentKey, Cultures = cultures };

    public static IndexNowWorkItem ForUrl(string url) => new() { Url = url };

    public static IndexNowWorkItem ForRemovedUrl(string url, IContent content, string? culture)
        => new() { Url = url, ChangeType = IndexNowChangeType.Removed, Content = content, Culture = culture };
}
