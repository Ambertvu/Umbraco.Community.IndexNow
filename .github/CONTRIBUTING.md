# Contributing Guidelines

Contributions to this package are most welcome!

## Umbraco versions

One source, one package per Umbraco major: 17.x for Umbraco 17 and 18.x for Umbraco 18. A build for one major does not run on another, even when the source compiles against both, so each has its own dependency range. Select a major with `-p:UmbracoMajor=18`; the default is 17.

```bash
dotnet test src/IndexNow.slnx -p:UmbracoMajor=18
```

A release is a tag such as `17.0.0` or `18.0.0`: the first number decides the Umbraco major it is built against. The workflow publishes through NuGet Trusted Publishing, so the repository holds no API key. Should the source ever need to differ per major, move to a branch per major.

## Test site

The test site does an unattended install on SQLite, check `appsettings.json` for the login details. It sends nothing to the real IndexNow API: its `Endpoint` points at a stand-in inside the site itself.

```bash
dotnet run --project src/IndexNow.TestSite --launch-profile Umbraco.Web.UI
```

Run the steps below in order against a clean database, and read what was submitted after each one. The batch delay on the test site is 2 seconds.

```bash
curl -X POST http://localhost:60399/__indexnow/scenario/setup
curl http://localhost:60399/__indexnow/mock
curl -X DELETE http://localhost:60399/__indexnow/mock
```

The steps and what each must submit:

| Step | Must submit |
|---|---|
| `setup` | Everything except `no-index`, `hidden-from-sitemap`, `no-template`, `hidden-child`, `unpublish-no-index`, `members-only`, the folders, and all of `www.hidden-demo.nl` and `www.container-demo.nl`. `allow-indexing-never-set` is there. |
| `republish` | `visible` |
| `rename` | The old and new URL |
| `rename-branch` | The old and new URLs of the parent, its child and its grandchild |
| `republish-parent` | Only the parent |
| `unpublish` | `unpublish-me` and `allow-indexing` |
| `trash` | `trash-me` |
| `trash-protected` | Nothing |
| `move` | The old and new URLs of the page and its child |
| `delete` | `delete-me` |
| `unpublish-parent` | `cycle-parent` and `cycle-child` |
| `republish-unpublished-parent` | `cycle-parent` and `cycle-child` |
| `noindex-on` | `visible`: search engines must learn that it changed |
| `republish-noindex` | Nothing |
| `unpublish-variant` | `variant-gone` in both languages |
| `unpublish-culture` | `variant-parent` in both languages and its child in Dutch |
| `republish-culture` | `variant-parent` and its child in both languages |
| `rename-above-folder` | The old and new URLs of `section` and of `in-folder` without `hidden-folder` in them |
| `folder-then-parent` | `section-renamed` |
| `change-domain` | The root and `page-on-two` on both `www.two-demo.nl` and `www.two-new-demo.nl` |

The `HiddenFolderUrlProvider` of the test site leaves `hidden-folder` out of URLs. Run the steps over https (`https://localhost:44399`), which catches URLs that depend on the request.

To start over, or to switch to another Umbraco major, stop the site and delete `src/IndexNow.TestSite/umbraco/Data`.
