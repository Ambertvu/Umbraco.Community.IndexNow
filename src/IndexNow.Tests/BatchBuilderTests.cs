using IndexNow.Submission;

namespace IndexNow.Tests;

public class BatchBuilderTests
{
    [Fact]
    public void Makes_one_batch_per_host()
    {
        var batches = IndexNowBatchBuilder.Build(
        [
            new Uri("https://www.one.nl/a/"),
            new Uri("https://www.two.nl/a/"),
            new Uri("https://WWW.ONE.NL/b/"),
        ]).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(["https://www.one.nl/a/", "https://www.one.nl/b/"], batches.Single(batch => batch.Host == "www.one.nl").Urls);
        Assert.Equal(["https://www.two.nl/a/"], batches.Single(batch => batch.Host == "www.two.nl").Urls);
    }

    [Fact]
    public void Splits_on_the_request_limit_and_removes_duplicates()
    {
        var urls = Enumerable.Range(0, 5).Select(i => new Uri($"https://www.one.nl/{i}/")).Append(new Uri("https://www.one.nl/0/"));

        var batches = IndexNowBatchBuilder.Build(urls, maxUrlsPerRequest: 2).ToList();

        Assert.Equal([2, 2, 1], batches.Select(batch => batch.Urls.Count));
    }

    [Fact]
    public void Uses_the_limit_of_the_protocol()
        => Assert.Equal(10_000, IndexNowBatchBuilder.MaxUrlsPerRequest);
}
