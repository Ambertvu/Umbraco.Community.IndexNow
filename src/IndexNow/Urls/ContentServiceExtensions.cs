using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Urls;

internal static class ContentServiceExtensions
{
    private const int PageSize = 500;

    /// <summary>Everything below a content item, a page at a time.</summary>
    public static IEnumerable<IContent> GetDescendants(this IContentService contentService, int id)
    {
        long pageIndex = 0;
        long total;
        do
        {
            var page = contentService.GetPagedDescendants(id, pageIndex++, PageSize, out total).ToList();
            foreach (IContent descendant in page)
            {
                yield return descendant;
            }
        }
        while (pageIndex * PageSize < total);
    }
}
