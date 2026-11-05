using System.Net;
using System.Text.Json;
using IndexNow.Configuration;
using IndexNow.Submission;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IndexNow.Tests;

public class ClientTests
{
    private static readonly IndexNowBatch _batch = new("www.example.com", ["https://www.example.com/a/", "https://www.example.com/b/"]);

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task Treats_200_and_202_as_accepted(HttpStatusCode status)
    {
        var handler = new StubHandler(status);
        Assert.True(await Client(handler).SubmitAsync(_batch, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Posts_the_payload_of_the_protocol()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        await Client(handler).SubmitAsync(_batch, CancellationToken.None);

        Assert.Equal(new Uri("https://api.indexnow.org/indexnow"), handler.LastUri);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        using JsonDocument json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("www.example.com", json.RootElement.GetProperty("host").GetString());
        Assert.Equal("abcd1234-key", json.RootElement.GetProperty("key").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("urlList").GetArrayLength());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)422)]
    public async Task Does_not_retry_a_refusal(HttpStatusCode status)
    {
        var handler = new StubHandler(status);
        Assert.False(await Client(handler).SubmitAsync(_batch, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Retries_a_temporary_failure_until_it_works(HttpStatusCode status)
    {
        var handler = new StubHandler(status, HttpStatusCode.OK);
        Assert.True(await Client(handler).SubmitAsync(_batch, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_the_last_retry()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        Assert.False(await Client(handler).SubmitAsync(_batch, CancellationToken.None));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Retries_a_connection_failure()
    {
        var handler = new StubHandler(HttpStatusCode.OK) { ThrowOnFirstCall = true };
        Assert.True(await Client(handler).SubmitAsync(_batch, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Stops_when_the_application_stops()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new StubHandler(HttpStatusCode.OK) { ThrowOnFirstCall = true, ThrowCancelled = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).SubmitAsync(_batch, cancelled.Token));
    }

    private static IndexNowClient Client(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            Options.Create(new IndexNowOptions { Key = "abcd1234-key" }),
            NullLogger<IndexNowClient>.Instance)
        {
            RetryDelays = [TimeSpan.Zero, TimeSpan.Zero],
        };

    private sealed class StubHandler(params HttpStatusCode[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public bool ThrowOnFirstCall { get; init; }

        public bool ThrowCancelled { get; init; }

        public Uri? LastUri { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Calls++;
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (ThrowOnFirstCall && call == 0)
            {
                throw ThrowCancelled ? new TaskCanceledException() : new HttpRequestException("Connection refused");
            }

            return new HttpResponseMessage(responses[Math.Min(ThrowOnFirstCall ? call - 1 : call, responses.Length - 1)]);
        }
    }
}
