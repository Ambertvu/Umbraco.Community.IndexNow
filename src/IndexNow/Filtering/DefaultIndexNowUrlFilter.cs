using IndexNow.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace IndexNow.Filtering;

/// <summary>The rules from configuration: document types, templates and noindex-style properties.</summary>
internal sealed class DefaultIndexNowUrlFilter : IIndexNowUrlFilter
{
    private readonly IContentService _contentService;
    private readonly IndexNowOptions _options;
    private readonly string[] _noIndexProperties;
    private readonly string[] _indexProperties;
    private readonly string[] _siteWideProperties;

    // Registered scoped and a scope lasts one batch, so a branch does not look up its root per page.
    private readonly Dictionary<int, IContent?> _roots = [];

    public DefaultIndexNowUrlFilter(IContentService contentService, IOptions<IndexNowOptions> options)
    {
        _contentService = contentService;
        _options = options.Value;

        var useDefaults = _options.UseDefaultProperties;
        _noIndexProperties = Combine(useDefaults ? IndexNowDefaults.NoIndexProperties : [], _options.NoIndexProperties);
        _indexProperties = Combine(useDefaults ? IndexNowDefaults.IndexProperties : [], _options.IndexProperties);
        _siteWideProperties = Combine(useDefaults ? IndexNowDefaults.SiteWideNoIndexProperties : [], _options.SiteWideNoIndexProperties);
    }

    public ValueTask<bool> ShouldSubmitAsync(IndexNowCandidate candidate, CancellationToken cancellationToken)
        => ValueTask.FromResult(candidate.Content is not { } content || IsIndexable(content, candidate.Culture));

    private bool IsIndexable(IContent content, string? culture)
    {
        if (_options.ExcludedDocumentTypes.Contains(content.ContentType.Alias, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_options.RequireTemplate && content.TemplateId is null)
        {
            return false;
        }

        if (_noIndexProperties.Any(alias => ReadBoolean(content, alias, culture) is true)
            || _indexProperties.Any(alias => ReadBoolean(content, alias, culture) is false))
        {
            return false;
        }

        return GetRoot(content) is not { } root
            || !_siteWideProperties.Any(alias => ReadBoolean(root, alias, culture) is true);
    }

    /// <returns>The published value, or <c>null</c> when the content has no such True/false property.</returns>
    private static bool? ReadBoolean(IContent content, string alias, string? culture)
    {
        if (!content.Properties.TryGetValue(alias, out IProperty? property)
            || property.PropertyType.PropertyEditorAlias != Constants.PropertyEditors.Aliases.Boolean)
        {
            return null;
        }

        var valueCulture = property.PropertyType.VariesByCulture() ? culture : null;
        return content.GetValue<bool>(alias, valueCulture, segment: null, published: true);
    }

    private IContent? GetRoot(IContent content)
    {
        // Path looks like -1,1061,1062: the system root, the site root, then its descendants.
        var parts = content.Path.Split(',');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var rootId) || rootId == Constants.System.RecycleBinContent)
        {
            return null;
        }

        if (rootId == content.Id)
        {
            return content;
        }

        if (!_roots.TryGetValue(rootId, out IContent? root))
        {
            root = _contentService.GetById(rootId);
            _roots[rootId] = root;
        }

        return root;
    }

    private static string[] Combine(IEnumerable<string> defaults, IEnumerable<string> configured)
        => defaults.Concat(configured).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
