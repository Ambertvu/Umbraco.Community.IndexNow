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
                case "unpublish":
                    _contentService.Unpublish(Find("Unpublish me"));
                    break;
                case "trash":
                    _contentService.MoveToRecycleBin(Find("Trash me"));
                    break;
                case "move":
                    _contentService.Move(Find("Move me"), Find("Target").Id);
                    break;
                case "delete":
                    _contentService.Delete(Find("Delete me"));
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
            CreatePage("Page on two", two);
            CreatePage("Page in hidden site", hidden);

            IContent noTemplate = CreatePage("No template", one);
            noTemplate.TemplateId = null;
            Publish(noTemplate);
        }

        private async Task<IContent> CreateSiteAsync(string name, string domain, params string[] on)
        {
            IContent root = CreatePage(name, parent: null, on);
            await services.GetRequiredService<IDomainService>().UpdateDomainsAsync(
                root.Key,
                new DomainsUpdateModel { DefaultIsoCode = Culture, Domains = [new DomainModel { DomainName = domain, IsoCode = Culture }] });
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
                if (descendants.FirstOrDefault(content => content.Name == name) is { } found)
                {
                    return found;
                }
            }

            throw new InvalidOperationException($"No content named {name}");
        }
    }
}
