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
    /// <summary>The page was published or moved and lives at this URL.</summary>
    Updated,

    /// <summary>The page no longer lives at this URL: unpublished, deleted, renamed or moved.</summary>
    Removed,
}
