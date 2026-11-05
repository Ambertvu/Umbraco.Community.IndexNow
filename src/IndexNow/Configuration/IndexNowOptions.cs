using System.Text.RegularExpressions;

namespace IndexNow.Configuration;

/// <summary>Settings of the <c>IndexNow</c> section in appsettings.</summary>
public sealed partial class IndexNowOptions
{
    public const string SectionName = "IndexNow";

    /// <summary>Kill switch. Without a valid <see cref="Key"/> the package does nothing either.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The key, 8-128 letters, digits or hyphens. It is public by design: it is served at <c>/{key}.txt</c>.</summary>
    public string? Key { get; set; }

    /// <summary>The endpoint to submit to. api.indexnow.org shares a submission with every participating engine.</summary>
    public string Endpoint { get; set; } = "https://api.indexnow.org/indexnow";

    /// <summary>Public address of a site without Umbraco domains, for example <c>https://www.example.com</c>.
    /// Without domains there is no public host to build a URL with, so these URLs are dropped when this is empty.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Submit https URLs, also for a domain that is set up in Umbraco as http.</summary>
    public bool ForceHttps { get; set; } = true;

    /// <summary>Submit once no change has come in for this long, which also gives Umbraco's caches time to catch up after a
    /// publish, so it is at least one second. Editors who keep publishing hold back a submission for at most ten times this.</summary>
    public int BatchDelaySeconds { get; set; } = 30;

    /// <summary>When not empty, only these hosts are submitted.</summary>
    public string[] Hosts { get; set; } = [];

    /// <summary>Document type aliases that are never submitted.</summary>
    public string[] ExcludedDocumentTypes { get; set; } = [];

    /// <summary>URL path prefixes that are never submitted, such as <c>/search</c>. Matches whole path segments.</summary>
    public string[] ExcludedPaths { get; set; } = [];

    /// <summary>Leave out pages without a template: they normally have no page to show.</summary>
    public bool RequireTemplate { get; set; } = true;

    /// <summary>Also use the common property aliases in <see cref="IndexNowDefaults"/>.</summary>
    public bool UseDefaultProperties { get; set; } = true;

    /// <summary>True/false properties that leave a page out when they are <c>true</c>, added to the defaults.</summary>
    public string[] NoIndexProperties { get; set; } = [];

    /// <summary>True/false properties that leave a page out when they are <c>false</c>, added to the defaults.</summary>
    public string[] IndexProperties { get; set; } = [];

    /// <summary>True/false properties that leave out a whole site when they are <c>true</c> on its root node, added to the defaults.</summary>
    public string[] SiteWideNoIndexProperties { get; set; } = [];

    internal TimeSpan BatchDelay => TimeSpan.FromSeconds(Math.Max(1, BatchDelaySeconds));

    internal TimeSpan MaxBatchWait => BatchDelay * 10;

    internal bool HasValidKey => Key is not null && KeyFormat().IsMatch(Key);

    internal bool IsActive => Enabled && HasValidKey;

    [GeneratedRegex("^[a-zA-Z0-9-]{8,128}$")]
    private static partial Regex KeyFormat();
}
