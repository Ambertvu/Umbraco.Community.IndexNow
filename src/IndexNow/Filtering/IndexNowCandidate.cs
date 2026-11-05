using Umbraco.Cms.Core.Models;

namespace IndexNow.Filtering;

/// <summary>A URL that is about to be submitted.</summary>
/// <param name="Url">The final absolute URL.</param>
/// <param name="ChangeType">Why it is submitted.</param>
/// <param name="Content">The content behind it; <c>null</c> for a URL added through <c>IIndexNowQueue.EnqueueUrl</c>.</param>
/// <param name="Culture">The culture of the URL, when known.</param>
public sealed record IndexNowCandidate(Uri Url, IndexNowChangeType ChangeType, IContent? Content, string? Culture);

public enum IndexNowChangeType
{
    /// <summary>The page was published or moved and lives at this URL. The content is what it is now.</summary>
    Updated,

    /// <summary>The page lived at this URL before the change, and usually no longer does: unpublished, deleted, renamed or
    /// moved. The content is what it was before the change. A URL is submitted when one candidate for it passes, so a page
    /// that stops being indexable is still submitted once.</summary>
    Removed,
}
