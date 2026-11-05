using Umbraco.Cms.Core.Models;

namespace IndexNow.Urls;

internal readonly record struct ResolvedUrl(string Url, string? Culture);

internal interface IIndexNowUrlResolver
{
    /// <summary>The canonical published URL of the content per culture; nothing when it has no published URL.</summary>
    IReadOnlyList<ResolvedUrl> Resolve(IContent content);
}
