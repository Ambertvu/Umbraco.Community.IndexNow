using IndexNow.Configuration;
using IndexNow.KeyFile;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IndexNow.Tests;

public class KeyFileTests
{
    private const string Key = "abcd1234-key";

    [Fact]
    public async Task Serves_the_key_as_plain_text()
    {
        (HttpContext context, var nextCalled) = await Request("GET", $"/{Key}.txt");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.StartsWith("text/plain", context.Response.ContentType);
        context.Response.Body.Position = 0;
        Assert.Equal(Key, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Answers_a_head_request_without_a_body()
    {
        (HttpContext context, var nextCalled) = await Request("HEAD", $"/{Key}.txt");

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("GET", "/other-file.txt")]
    [InlineData("GET", "/" + Key)]
    [InlineData("POST", "/" + Key + ".txt")]
    public async Task Leaves_other_requests_alone(string method, string path)
    {
        (_, var nextCalled) = await Request(method, path);
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData(false, Key)]
    [InlineData(true, null)]
    [InlineData(true, "short")]
    [InlineData(true, "not valid!")]
    public async Task Serves_nothing_when_switched_off_or_without_a_valid_key(bool enabled, string? key)
    {
        (_, var nextCalled) = await Request("GET", $"/{key}.txt", new IndexNowOptions { Enabled = enabled, Key = key });
        Assert.True(nextCalled);
    }

    private static async Task<(HttpContext Context, bool NextCalled)> Request(string method, string path, IndexNowOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new IndexNowKeyFileMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(options ?? new IndexNowOptions { Key = Key }));

        await middleware.InvokeAsync(context);
        return (context, nextCalled);
    }
}
