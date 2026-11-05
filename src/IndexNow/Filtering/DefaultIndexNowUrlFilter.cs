using IndexNow.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace IndexNow.Filtering;

/// <summary>The built-in rules: document types, templates, noindex-style properties and public access.</summary>
internal sealed class DefaultIndexNowUrlFilter : IIndexNowUrlFilter
{
    private readonly IContentService _contentService;
    private readonly IPublicAccessService _publicAccessService;
    private readonly IDomainService _domainService;
    private readonly IndexNowOptions _options;
    private readonly string[] _noIndexProperties;
    private readonly string[] _indexProperties;
    private readonly string[] _siteWideProperties;

    // Registered scoped and a scope lasts one batch, so a branch does not look these up per page.
    private readonly Dictionary<int, IContent?> _ancestors = [];
    private HashSet<int>? _domainRootIds;

    public DefaultIndexNowUrlFilter(IContentService contentService, IPublicAccessService publicAccessService, IDomainService domainService, IOptions<IndexNowOptions> options)
    {
        _contentService = contentService;
        _publicAccessService = publicAccessService;
        _domainService = domainService;
        _options = options.Value;

        var useDefaults = _options.UseDefaultProperties;
        _noIndexProperties = Combine(useDefaults ? IndexNowDefaults.NoIndexProperties : [], _options.NoIndexProperties);
        _indexProperties = Combine(useDefaults ? IndexNowDefaults.IndexProperties : [], _options.IndexProperties);
        _siteWideProperties = Combine(useDefaults ? IndexNowDefaults.SiteWideNoIndexProperties : [], _options.SiteWideNoIndexProperties);
    }

    public async ValueTask<bool> ShouldSubmitAsync(IndexNowCandidate candidate, CancellationToken cancellationToken)
        => candidate.Content is not { } content || await IsIndexableAsync(content, candidate.Culture);

    private async ValueTask<bool> IsIndexableAsync(IContent content, string? culture)
    {
        if (_options.ExcludedDocumentTypes.Contains(content.ContentType.Alias, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_options.RequireTemplate && content.TemplateId is null)
        {
            return false;
        }

        if (_noIndexProperties.Any(alias => ReadBoolean(content, alias, culture, published: true) is true)
            || _indexProperties.Any(alias => ReadBoolean(content, alias, culture, published: true) is false))
        {
            return false;
        }

        // Announcing a page behind a login would give away its address.
        if (_publicAccessService.IsProtected(content).Success)
        {
            return false;
        }

        // A whole site that was unpublished has no published values left, and its pages must not be announced either.
        return await GetSiteRootAsync(content) is not { } root
            || !_siteWideProperties.Any(alias => ReadBoolean(root, alias, culture, published: root.Published) is true);
    }

    /// <returns>The value, or <c>null</c> when the content has no such True/false property or it was never set.</returns>
    private static bool? ReadBoolean(IContent content, string alias, string? culture, bool published)
    {
        if (!content.Properties.TryGetValue(alias, out IProperty? property)
            || property.PropertyType.PropertyEditorAlias != Constants.PropertyEditors.Aliases.Boolean)
        {
            return null;
        }

        var valueCulture = property.PropertyType.VariesByCulture() ? culture : null;
        var value = property.GetValue(valueCulture, segment: null, published);
        if (value is null or "")
        {
            return null;
        }

        Attempt<bool> converted = value.TryConvertTo<bool>();
        return converted.Success ? converted.Result : null;
    }

    /// <summary>
    /// The node with the closest domain. Without a domain, the highest node with a template: a folder above the home page
    /// holds no page of its own.
    /// </summary>
    private async ValueTask<IContent?> GetSiteRootAsync(IContent content)
    {
        // Path looks like -1,1061,1062: the system root, then from the top down to the content itself.
        var path = content.Path.Split(',').Select(id => int.TryParse(id, out var parsed) ? parsed : 0).ToArray();
        if (path.Contains(Constants.System.RecycleBinContent))
        {
            return null;
        }

        var ids = path.Where(id => id > 0).ToArray();
        if (ids.Length == 0)
        {
            return null;
        }

        _domainRootIds ??= (await _domainService.GetAllAsync(includeWildcards: false))
            .Select(domain => domain.RootContentId)
            .OfType<int>()
            .ToHashSet();

        var closestDomain = Array.FindLastIndex(ids, _domainRootIds.Contains);
        return closestDomain >= 0
            ? GetAncestorOrSelf(ids[closestDomain], content)
            : ids.Select(id => GetAncestorOrSelf(id, content)).FirstOrDefault(node => node?.TemplateId is not null)
                ?? GetAncestorOrSelf(ids[0], content);
    }

    private IContent? GetAncestorOrSelf(int id, IContent content)
    {
        if (id == content.Id)
        {
            return content;
        }

        if (!_ancestors.TryGetValue(id, out IContent? ancestor))
        {
            ancestor = _contentService.GetById(id);
            _ancestors[id] = ancestor;
        }

        return ancestor;
    }

    private static string[] Combine(IEnumerable<string> defaults, IEnumerable<string> configured)
        => defaults.Concat(configured).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
