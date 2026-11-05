using IndexNow.Configuration;

namespace IndexNow.Urls;

/// <summary>Turns what Umbraco gives us into the URL to submit, or rejects it.</summary>
internal static class IndexNowUrlNormalizer
{
    private static readonly string[] _nonPublicSuffixes = [".localhost", ".local", ".test", ".example", ".invalid", ".internal", ".lan"];

    /// <returns>The URL to submit, or <c>null</c> when it must not be submitted.</returns>
    public static Uri? Normalize(string url, IndexNowOptions options)
    {
        Uri? baseUri = null;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? parsedBase))
        {
            baseUri = parsedBase;
        }

        Uri? uri;
        if (url.StartsWith('/'))
        {
            if (baseUri is null)
            {
                return null;
            }

            uri = new Uri(baseUri, url);
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
        {
            uri = absolute;
        }
        else
        {
            return null;
        }

        // Outside a request Umbraco builds domainless URLs on http://localhost.
        if (!IsPublicHost(uri))
        {
            if (baseUri is null || !IsPublicHost(baseUri))
            {
                return null;
            }

            uri = new Uri(baseUri, uri.PathAndQuery);
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        if (options.ForceHttps && builder.Scheme == Uri.UriSchemeHttp)
        {
            builder.Scheme = Uri.UriSchemeHttps;
            builder.Port = -1;
        }

        Uri result = builder.Uri;
        return IsAllowedHost(result, options) && !IsExcludedPath(result, options) ? result : null;
    }

    private static bool IsPublicHost(Uri uri)
    {
        if (uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = uri.Host;
        return host.Contains('.') && !_nonPublicSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedHost(Uri uri, IndexNowOptions options)
        => options.Hosts.Length == 0 || options.Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    private static bool IsExcludedPath(Uri uri, IndexNowOptions options)
    {
        var path = uri.AbsolutePath;
        foreach (var excluded in options.ExcludedPaths)
        {
            var prefix = "/" + excluded.Trim('/');
            if (prefix == "/")
            {
                continue;
            }

            if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
