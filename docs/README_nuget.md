# IndexNow for Umbraco

[![Downloads](https://img.shields.io/nuget/dt/Umbraco.Community.IndexNow?color=cc9900)](https://www.nuget.org/packages/Umbraco.Community.IndexNow/)
[![NuGet](https://img.shields.io/nuget/vpre/Umbraco.Community.IndexNow?color=0273B3)](https://www.nuget.org/packages/Umbraco.Community.IndexNow)
[![GitHub license](https://img.shields.io/github/license/Ambertvu/Umbraco.Community.IndexNow?color=8AB803)](https://github.com/Ambertvu/Umbraco.Community.IndexNow/blob/main/LICENSE)

Tell Bing and the other IndexNow search engines about changed pages the moment an editor publishes them. Use version 17.x on Umbraco 17 and 18.x on Umbraco 18.

- Submits pages that are published, renamed, moved, unpublished, trashed or deleted, including the branch below them.
- Works across multiple domains and cultures.
- Leaves out noindex pages, pages without a template and non-public hosts. Add your own rules with a filter.
- No backoffice, no database tables: add the package and a key.

```json
{
  "IndexNow": {
    "Key": "c5d1f6a2-7b34-4e19-9a60-2f8e1d7c3b45"
  }
}
```

Without a key the package does nothing, so keep the key out of your local and acceptance configuration.

See the [documentation on GitHub](https://github.com/Ambertvu/Umbraco.Community.IndexNow) for all settings.
