# IndexNow for Umbraco

[![Downloads](https://img.shields.io/nuget/dt/Umbraco.Community.IndexNow?color=cc9900)](https://www.nuget.org/packages/Umbraco.Community.IndexNow/)
[![NuGet](https://img.shields.io/nuget/vpre/Umbraco.Community.IndexNow?color=0273B3)](https://www.nuget.org/packages/Umbraco.Community.IndexNow)
[![GitHub license](https://img.shields.io/github/license/Ambertvu/Umbraco.Community.IndexNow?color=8AB803)](https://github.com/Ambertvu/Umbraco.Community.IndexNow/blob/main/LICENSE)

Tell Bing and the other [IndexNow](https://www.indexnow.org/) search engines about changed pages the moment an editor publishes them, instead of waiting for the next crawl. Google does not take part in IndexNow.

- Submits pages that are published, renamed, moved, unpublished, trashed or deleted, including the branch below them.
- Works across multiple domains and cultures.
- Leaves out noindex pages, pages without a template and non-public hosts.
- No backoffice, no database tables. A failure is logged and never gets in the way of a publish.

## Installation

Install the version that matches your Umbraco major: 17.x for Umbraco 17, 18.x for Umbraco 18.

```bash
dotnet add package Umbraco.Community.IndexNow
```

Add a key to your production configuration. It may hold 8-128 letters, digits or hyphens, so a GUID works.

```json
{
  "IndexNow": {
    "Key": "c5d1f6a2-7b34-4e19-9a60-2f8e1d7c3b45"
  }
}
```

That is it. The package serves the key at `/{key}.txt` on every domain, which is how search engines verify a key, and it needs no file on disk.

**Without a key the package does nothing**, so leave the key out of your local and acceptance configuration. If you use one configuration everywhere, set `IndexNow:Enabled` to `false` outside production, or limit `IndexNow:Hosts` to your live domains. The key is public by design, so it is not a secret.

### A site without Umbraco domains

Outside a request Umbraco cannot know the public address of a site without domains (Culture and Hostnames), so these pages are skipped until you set `BaseUrl`:

```json
{
  "IndexNow": {
    "Key": "c5d1f6a2-7b34-4e19-9a60-2f8e1d7c3b45",
    "BaseUrl": "https://www.example.com"
  }
}
```

## What is left out

A URL is only submitted when all of these hold:

- It is on a public host: not `localhost`, an IP address, a name without a dot, or `.local`, `.test` and the like.
- The page has a template (`RequireTemplate`).
- Its document type and path are not excluded (`ExcludedDocumentTypes`, `ExcludedPaths`).
- No noindex property says otherwise, see below.
- Every filter of your own agrees.

### Noindex properties

Only True/false properties count, and a document type without them is not affected.

| Rule | Default aliases | Add your own |
|---|---|---|
| Left out when `true` | `noIndex`, `doNotIndex`, `hideInSeoSitemap`, `hideFromSearchEngines`, `excludeFromSitemap` | `NoIndexProperties` |
| Left out when `false` | `index`, `indexable`, `allowIndexing`, `canBeIndexed` | `IndexProperties` |
| Whole site left out when `true` on its root node | `noIndex`, `doNotIndex` | `SiteWideNoIndexProperties` |

Aliases are not case sensitive. `umbracoNaviHide` is deliberately not on the list: it hides a page from navigation, not from search engines. Set `UseDefaultProperties` to `false` to use only your own aliases.

### Filters of your own

For anything else, such as a robots dropdown, add a filter. The candidate holds the URL, the content, the culture and whether the URL is new or gone.

```csharp
public class PrivateSectionFilter : IIndexNowUrlFilter
{
    public ValueTask<bool> ShouldSubmitAsync(IndexNowCandidate candidate, CancellationToken cancellationToken)
        => ValueTask.FromResult(candidate.Content?.ContentType.Alias != "memberPage");
}

public class MyComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddIndexNowUrlFilter<PrivateSectionFilter>();
}
```

## Pages Umbraco does not know about

Inject `IIndexNowQueue` to add a URL yourself, such as a page served by a custom route, or to submit the URLs of a content item after a change that is not a publish.

```csharp
indexNowQueue.EnqueueUrl("https://www.example.com/products/some-product/");
indexNowQueue.EnqueueContent(contentKey);
```

## All settings

| Setting | Default | |
|---|---|---|
| `Enabled` | `true` | Kill switch. |
| `Key` | | Required. Without a valid key the package does nothing. |
| `Endpoint` | `https://api.indexnow.org/indexnow` | Shares a submission with every participating engine. |
| `BaseUrl` | | Public address of a site without Umbraco domains. |
| `ForceHttps` | `true` | Submit https URLs, also when Umbraco builds http ones. |
| `BatchDelaySeconds` | `30` | How long to collect changes before submitting. |
| `Hosts` | all | When set, only these hosts are submitted. |
| `ExcludedDocumentTypes` | none | Document type aliases. |
| `ExcludedPaths` | none | Path prefixes, matched on whole segments: `/search` also covers `/search/results`, not `/searching`. |
| `RequireTemplate` | `true` | Leave out pages without a template. |
| `UseDefaultProperties` | `true` | Use the default aliases above. |
| `NoIndexProperties` | none | Added to the defaults. |
| `IndexProperties` | none | Added to the defaults. |
| `SiteWideNoIndexProperties` | none | Added to the defaults. |

## Good to know

- **Check that it works:** look for `IndexNow accepted 3 URLs for www.example.com (202)` in the log. The URLs themselves are logged on the `Debug` level under the `IndexNow` namespace. A refusal is logged as a warning with a hint.
- **A new key takes time:** the endpoint answers `202` until the key has been verified.
- **Retries:** temporary failures (`429`, `5xx`, no connection) are tried three times. After that the URLs are dropped.
- **Restarts:** the queue lives in memory, so changes still waiting during a restart are lost. Search engines pick those up from the sitemap as they always did.
- **Load balancing:** content notifications fire on the server that does the publish, so a URL is submitted once.
- **A cancelled change:** URLs that are about to disappear are read before the change. If another handler then cancels it, a live URL gets submitted once more, which does no harm.
- **Non-canonical domains:** only the URL Umbraco itself generates for a page is submitted, not those of other domains on the same node.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
