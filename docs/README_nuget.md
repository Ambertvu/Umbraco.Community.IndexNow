# IndexNow for Umbraco

[![Downloads](https://img.shields.io/nuget/dt/Umbraco.Community.IndexNow?color=cc9900)](https://www.nuget.org/packages/Umbraco.Community.IndexNow/)
[![NuGet](https://img.shields.io/nuget/vpre/Umbraco.Community.IndexNow?color=0273B3)](https://www.nuget.org/packages/Umbraco.Community.IndexNow)
[![GitHub license](https://img.shields.io/github/license/Ambertvu/Umbraco.Community.IndexNow?color=8AB803)](https://github.com/Ambertvu/Umbraco.Community.IndexNow/blob/main/LICENSE)

Tell Bing and the other IndexNow search engines about changed pages the moment an editor publishes them. Use version 17.x on Umbraco 17 and 18.x on Umbraco 18.

- **URLs that change or disappear:** a rename or move submits the old and new URLs of the page and everything below it. Unpublishing, trashing or deleting a page submits its branch.
- **Multiple domains and cultures:** addresses come from Culture and Hostnames, per culture, so there is no domain to configure.
- **Pages that should stay out:** noindex pages, pages without a template and non-public hosts are left out. So are pages behind Public Access: announcing one would give away its address. Add your own rules with a filter.
- **Only a key to set up:** the package serves it on every domain, so there is no file to place. No backoffice, no database tables.
- **A publish does not wait for IndexNow:** changes are sent in batches in the background, and a failure never gets in the way of a publish.

```json
{
  "IndexNow": {
    "Key": "c5d1f6a2-7b34-4e19-9a60-2f8e1d7c3b45"
  }
}
```

Without a key the package does nothing, so keep the key out of your local and acceptance configuration.

See the [documentation on GitHub](https://github.com/Ambertvu/Umbraco.Community.IndexNow) for all settings.
