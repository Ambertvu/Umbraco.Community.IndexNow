using IndexNow.Configuration;
using IndexNow.Urls;

namespace IndexNow.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://www.example.com/page/", "https://www.example.com/page/")]
    [InlineData("http://www.example.com/page/", "https://www.example.com/page/")]
    [InlineData("https://www.example.com/page/#top", "https://www.example.com/page/")]
    [InlineData("https://www.example.com/page/?a=1", "https://www.example.com/page/?a=1")]
    public void Keeps_public_urls_on_https(string url, string expected)
        => Assert.Equal(expected, IndexNowUrlNormalizer.Normalize(url, new IndexNowOptions())?.AbsoluteUri);

    [Fact]
    public void Leaves_http_alone_when_https_is_not_forced()
        => Assert.Equal(
            "http://www.example.com/page/",
            IndexNowUrlNormalizer.Normalize("http://www.example.com/page/", new IndexNowOptions { ForceHttps = false })?.AbsoluteUri);

    [Theory]
    [InlineData("http://localhost/page/")]
    [InlineData("https://localhost:44398/page/")]
    [InlineData("http://127.0.0.1/page/")]
    [InlineData("http://[::1]/page/")]
    [InlineData("https://intranet/page/")]
    [InlineData("https://site.localhost/page/")]
    [InlineData("https://site.local/page/")]
    [InlineData("https://site.test/page/")]
    [InlineData("/page/")]
    [InlineData("#")]
    [InlineData("ftp://www.example.com/page/")]
    public void Rejects_what_is_not_public_without_a_base_url(string url)
        => Assert.Null(IndexNowUrlNormalizer.Normalize(url, new IndexNowOptions()));

    [Theory]
    [InlineData("http://localhost/page/?a=1")]
    [InlineData("/page/?a=1")]
    public void Rebases_domainless_urls_on_the_base_url(string url)
        => Assert.Equal(
            "https://www.example.com/page/?a=1",
            IndexNowUrlNormalizer.Normalize(url, new IndexNowOptions { BaseUrl = "https://www.example.com" })?.AbsoluteUri);

    [Fact]
    public void Does_not_rebase_a_public_url_of_another_domain()
        => Assert.Equal(
            "https://www.other.com/page/",
            IndexNowUrlNormalizer.Normalize("https://www.other.com/page/", new IndexNowOptions { BaseUrl = "https://www.example.com" })?.AbsoluteUri);

    [Fact]
    public void A_local_base_url_does_not_make_a_url_public()
        => Assert.Null(IndexNowUrlNormalizer.Normalize("/page/", new IndexNowOptions { BaseUrl = "https://localhost:44398" }));

    [Theory]
    [InlineData("https://www.example.com/", true)]
    [InlineData("https://WWW.EXAMPLE.COM/", true)]
    [InlineData("https://acc.example.com/", false)]
    public void Honours_the_host_allowlist(string url, bool allowed)
    {
        var options = new IndexNowOptions { Hosts = ["www.example.com"] };
        Assert.Equal(allowed, IndexNowUrlNormalizer.Normalize(url, options) is not null);
    }

    [Theory]
    [InlineData("https://www.example.com/search", false)]
    [InlineData("https://www.example.com/search/", false)]
    [InlineData("https://www.example.com/SEARCH/results/", false)]
    [InlineData("https://www.example.com/searching/", true)]
    [InlineData("https://www.example.com/", true)]
    public void Excludes_paths_on_whole_segments(string url, bool allowed)
    {
        var options = new IndexNowOptions { ExcludedPaths = ["/search/"] };
        Assert.Equal(allowed, IndexNowUrlNormalizer.Normalize(url, options) is not null);
    }
}
