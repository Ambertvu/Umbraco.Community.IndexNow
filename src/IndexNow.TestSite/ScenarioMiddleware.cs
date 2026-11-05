using System.Collections.Concurrent;
using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace IndexNow.TestSite;

/// <summary>
/// GET/DELETE /__indexnow/mock reads or clears what was submitted, POST /__indexnow/mock is the IndexNow endpoint and
/// POST /__indexnow/scenario/{step} changes content through the same services the backoffice uses.
/// </summary>
internal sealed class ScenarioMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
{
    private const string MockPath = "/__indexnow/mock";
    private const string ScenarioPath = "/__indexnow/scenario/";

    private static readonly ConcurrentQueue<JsonElement> _submissions = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path == MockPath)
        {
            await HandleMock(context);
        }
        else if (path.StartsWith(ScenarioPath, StringComparison.Ordinal) && HttpMethods.IsPost(context.Request.Method))
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            var message = await new Scenario(scope.ServiceProvider).RunAsync(path[ScenarioPath.Length..]);
            await context.Response.WriteAsync(message);
        }
        else
        {
            await next(context);
        }
    }

    private static async Task HandleMock(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method))
        {
            _submissions.Enqueue(await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body));
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        }
        else if (HttpMethods.IsDelete(context.Request.Method))
        {
            _submissions.Clear();
        }
        else
        {
            await context.Response.WriteAsJsonAsync(_submissions.ToArray());
        }
    }

    private sealed class Scenario(IServiceProvider services)
    {
        private const string Culture = "en-US";
        private const string OtherCulture = "nl-NL";

        private readonly IContentService _contentService = services.GetRequiredService<IContentService>();

        public async Task<string> RunAsync(string step)
        {
            switch (step)
            {
                case "setup":
                    await SetUpAsync();
                    break;
                case "republish":
                    Publish(Find("Visible"));
                    break;
                case "rename":
                    IContent renamed = Find("Rename me");
                    renamed.Name = "Renamed";
                    Publish(renamed);
                    break;
                case "rename-branch":
                    IContent renamedParent = Find("Rename parent");
                    renamedParent.Name = "Renamed parent";
                    Publish(renamedParent);
                    break;
                case "republish-parent":
                    Publish(Find("Renamed parent"));
                    break;
                case "unpublish":
                    _contentService.Unpublish(Find("Unpublish me"));
                    _contentService.Unpublish(Find("Unpublish no index"));
                    _contentService.Unpublish(Find("Allow indexing"));
                    break;
                case "trash":
                    _contentService.MoveToRecycleBin(Find("Trash me"));
                    break;
                case "trash-protected":
                    _contentService.MoveToRecycleBin(Find("Members only"));
                    break;
                case "move":
                    _contentService.Move(Find("Move me"), Find("Target").Id);
                    break;
                case "delete":
                    _contentService.Delete(Find("Delete me"));
                    break;
                case "unpublish-parent":
                    _contentService.Unpublish(Find("Cycle parent"));
                    break;
                case "republish-unpublished-parent":
                    Publish(Find("Cycle parent"));
                    break;
                case "noindex-on":
                    IContent turnedOff = Find("Visible");
                    turnedOff.SetValue("noIndex", true);
                    Publish(turnedOff);
                    break;
                case "republish-noindex":
                    Publish(Find("Visible"));
                    break;
                case "unpublish-variant":
                    _contentService.Unpublish(Find("Variant gone"), "*");
                    break;
                case "unpublish-culture":
                    _contentService.Unpublish(Find("Variant parent"), OtherCulture);
                    break;
                case "republish-culture":
                    _contentService.Publish(Find("Variant parent"), [OtherCulture]);
                    break;
                case "republish-one-culture":
                    _contentService.Publish(Find("Variant parent"), [Culture]);
                    break;
                case "invariant-one-culture":
                    IContent shared = Find("Variant parent");
                    shared.SetValue("sharedValue", true);
                    _contentService.Save(shared);
                    _contentService.Publish(shared, [Culture]);
                    break;
                case "rename-one-culture":
                    IContent renamedInOne = Find("Variant parent");
                    renamedInOne.SetCultureName("Variant parent renamed NL", OtherCulture);
                    _contentService.Save(renamedInOne);
                    _contentService.Publish(renamedInOne, [OtherCulture]);
                    break;
                case "unpublish-variant-parent":
                    _contentService.Unpublish(Find("Variant parent"), "*");
                    break;
                case "republish-variant-parent-one-culture":
                    _contentService.Publish(Find("Variant parent"), [Culture]);
                    break;
                case "rename-above-folder":
                    IContent section = Find("Section");
                    section.Name = "Section renamed";
                    Publish(section);
                    break;
                case "folder-then-parent":
                    Publish(Find("Hidden folder"));
                    Publish(Find("Section renamed"));
                    break;
                case "change-domain":
                    await services.GetRequiredService<IDomainService>().UpdateDomainsAsync(
                        Find("Site two").Key,
                        new DomainsUpdateModel { DefaultIsoCode = Culture, Domains = [new DomainModel { DomainName = "www.two-new-demo.nl", IsoCode = Culture }] });
                    break;
                default:
                    return $"Unknown step {step}";
            }

            return $"Ran {step}";
        }

        private async Task SetUpAsync()
        {
            var shortStringHelper = services.GetRequiredService<IShortStringHelper>();
            IDataType checkbox = (await services.GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.CheckboxGuid))!;

            ITemplate template = (await services.GetRequiredService<ITemplateService>()
                .CreateAsync("Test page", "testPage", "<h1>@Model.Name</h1>", Constants.Security.SuperUserKey)).Result;

            var page = new ContentType(shortStringHelper, Constants.System.Root) { Alias = "testPage", Name = "Test page", AllowedAsRoot = true };
            page.AddPropertyType(new PropertyType(shortStringHelper, checkbox, "noIndex") { Name = "No index" });
            page.AddPropertyType(new PropertyType(shortStringHelper, checkbox, "hideInSEOSitemap") { Name = "Hide in SEO sitemap" });
            page.AllowedTemplates = [template];
            page.SetDefaultTemplate(template);

            IContentTypeService contentTypeService = services.GetRequiredService<IContentTypeService>();
            await contentTypeService.CreateAsync(page, Constants.Security.SuperUserKey);
            page.AllowedContentTypes = [new ContentTypeSort(page.Key, 0, page.Alias)];
            await contentTypeService.UpdateAsync(page, Constants.Security.SuperUserKey);

            var allowing = new ContentType(shortStringHelper, Constants.System.Root) { Alias = "allowingPage", Name = "Allowing page" };
            allowing.AddPropertyType(new PropertyType(shortStringHelper, checkbox, "allowIndexing") { Name = "Allow indexing" });
            allowing.AllowedTemplates = [template];
            allowing.SetDefaultTemplate(template);
            await contentTypeService.CreateAsync(allowing, Constants.Security.SuperUserKey);

            var folder = new ContentType(shortStringHelper, Constants.System.Root) { Alias = "testFolder", Name = "Test folder", AllowedAsRoot = true };
            await contentTypeService.CreateAsync(folder, Constants.Security.SuperUserKey);

            var variant = new ContentType(shortStringHelper, Constants.System.Root)
            {
                Alias = "variantPage",
                Name = "Variant page",
                AllowedAsRoot = true,
                Variations = ContentVariation.Culture,
            };
            variant.AddPropertyType(new PropertyType(shortStringHelper, checkbox, "sharedValue") { Name = "Shared value" });
            variant.AllowedTemplates = [template];
            variant.SetDefaultTemplate(template);
            await contentTypeService.CreateAsync(variant, Constants.Security.SuperUserKey);
            await services.GetRequiredService<ILanguageService>().CreateAsync(new Language(OtherCulture, "Dutch"), Constants.Security.SuperUserKey);

            IContent one = await CreateSiteAsync("Site one", "www.one-demo.nl");
            IContent two = await CreateSiteAsync("Site two", "www.two-demo.nl");
            IContent hidden = await CreateSiteAsync("Site hidden", "www.hidden-demo.nl", "noIndex");

            CreatePage("Visible", one);
            CreatePage("No index", one, "noIndex");
            CreatePage("Hidden from sitemap", one, "hideInSEOSitemap");
            CreatePage("Rename me", one);
            CreatePage("Unpublish me", one);
            CreatePage("Trash me", one);
            CreatePage("Delete me", one);
            CreatePage("Target", one);
            IContent branch = CreatePage("Move me", one);
            CreatePage("Moved child", branch);
            IContent renameParent = CreatePage("Rename parent", one);
            IContent renameChild = CreatePage("Child", renameParent);
            CreatePage("Grandchild", renameChild);
            CreatePage("Hidden child", renameParent, "noIndex");
            CreatePage("Unpublish no index", one, "noIndex");
            CreatePage("Cycle child", CreatePage("Cycle parent", one));

            IContent allowed = _contentService.Create("Allow indexing", one.Id, "allowingPage");
            allowed.SetValue("allowIndexing", true);
            Publish(allowed);

            // Added after the pages were made in real life, so never set.
            IContent neverSet = _contentService.Create("Allow indexing never set", one.Id, "allowingPage");
            Publish(neverSet);

            IContent members = _contentService.Create("Members only", one.Id, "testPage");
            _contentService.Save(members);
            services.GetRequiredService<IPublicAccessService>().Save(
                new PublicAccessEntry(members, one, one, [new PublicAccessRule { RuleType = "test", RuleValue = "test" }]));
            Publish(members);

            CreatePage("Page on two", two);
            CreatePage("Page in hidden site", hidden);

            IContent noTemplate = CreatePage("No template", one);
            noTemplate.TemplateId = null;
            Publish(noTemplate);

            // A folder above the home page must not be mistaken for the root of the site.
            IContent containerHome = await CreateSiteAsync("Container home", "www.container-demo.nl", "noIndex", parent: CreateFolder("Sites", null));
            CreatePage("Page in container", containerHome);

            // See HiddenFolderUrlProvider.
            IContent folderHome = await CreateSiteAsync("Folder home", "www.folder-demo.nl");
            CreatePage("In folder", CreateFolder("Hidden folder", CreatePage("Section", folderHome)));

            IContent variantRoot = await CreateVariantSiteAsync();
            CreateVariant("Variant child", CreateVariant("Variant parent", variantRoot));
            CreateVariant("Variant gone", variantRoot);
        }

        private async Task<IContent> CreateSiteAsync(string name, string domain, string? on = null, IContent? parent = null)
        {
            IContent root = on is null ? CreatePage(name, parent) : CreatePage(name, parent, on);
            await services.GetRequiredService<IDomainService>().UpdateDomainsAsync(
                root.Key,
                new DomainsUpdateModel { DefaultIsoCode = Culture, Domains = [new DomainModel { DomainName = domain, IsoCode = Culture }] });
            return root;
        }

        private async Task<IContent> CreateVariantSiteAsync()
        {
            IContent root = CreateVariant("Variant site", parent: null);
            await services.GetRequiredService<IDomainService>().UpdateDomainsAsync(
                root.Key,
                new DomainsUpdateModel
                {
                    DefaultIsoCode = Culture,
                    Domains =
                    [
                        new DomainModel { DomainName = "www.variant-demo.nl/en", IsoCode = Culture },
                        new DomainModel { DomainName = "www.variant-demo.nl/nl", IsoCode = OtherCulture },
                    ],
                });
            return root;
        }

        private IContent CreatePage(string name, IContent? parent, params string[] on)
        {
            IContent content = _contentService.Create(name, parent?.Id ?? Constants.System.Root, "testPage");
            foreach (var alias in on)
            {
                content.SetValue(alias, true);
            }

            Publish(content);
            return content;
        }

        private IContent CreateFolder(string name, IContent? parent)
        {
            IContent content = _contentService.Create(name, parent?.Id ?? Constants.System.Root, "testFolder");
            Publish(content);
            return content;
        }

        private IContent CreateVariant(string name, IContent? parent)
        {
            IContent content = _contentService.Create(name, parent?.Id ?? Constants.System.Root, "variantPage");
            content.SetCultureName(name, Culture);
            content.SetCultureName($"{name} NL", OtherCulture);
            Publish(content);
            return content;
        }

        private void Publish(IContent content)
        {
            _contentService.Save(content);
            _contentService.Publish(content, ["*"]);
        }

        private IContent Find(string name)
        {
            foreach (IContent root in _contentService.GetRootContent())
            {
                IEnumerable<IContent> descendants = _contentService.GetPagedDescendants(root.Id, 0, 100, out _);
                if (descendants.Prepend(root).FirstOrDefault(content => content.Name == name) is { } found)
                {
                    return found;
                }
            }

            throw new InvalidOperationException($"No content named {name}");
        }
    }
}
