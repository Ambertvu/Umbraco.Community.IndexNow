namespace IndexNow.Configuration;

/// <summary>
/// Common aliases of true/false properties that keep a page out of search engines. Only properties that use the
/// True/false editor count, and a document type without one of these properties is not affected.
/// <c>umbracoNaviHide</c> is deliberately missing: it hides a page from navigation, not from search engines.
/// </summary>
public static class IndexNowDefaults
{
    /// <summary>The page is left out when one of these is <c>true</c>.</summary>
    public static readonly IReadOnlyList<string> NoIndexProperties =
    [
        "noIndex",
        "doNotIndex",
        "hideInSeoSitemap",
        "hideFromSearchEngines",
        "excludeFromSitemap",
    ];

    /// <summary>The page is left out when one of these is <c>false</c>.</summary>
    public static readonly IReadOnlyList<string> IndexProperties =
    [
        "index",
        "indexable",
        "allowIndexing",
        "canBeIndexed",
    ];

    /// <summary>The whole site is left out when one of these is <c>true</c> on its root node.
    /// Not <c>hideInSeoSitemap</c>: hiding the home page from a sitemap says nothing about its children.</summary>
    public static readonly IReadOnlyList<string> SiteWideNoIndexProperties =
    [
        "noIndex",
        "doNotIndex",
    ];
}
