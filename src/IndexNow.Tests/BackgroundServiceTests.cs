using IndexNow.Configuration;
using IndexNow.Queue;
using IndexNow.Submission;
using IndexNow.Urls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Tests;

public class BackgroundServiceTests
{
    [Fact]
    public async Task A_cancellation_that_is_not_a_shutdown_does_not_stop_the_service()
    {
        var options = Options.Create(new IndexNowOptions { Key = "abcd1234-key", BatchDelaySeconds = 1 });
        var queue = new IndexNowQueue(options);
        var failing = Guid.NewGuid();
        var client = new Mock<IIndexNowClient>();
        var contentService = new Mock<IContentService>();
        contentService.Setup(service => service.GetById(failing)).Throws(new TaskCanceledException());

        ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton(contentService.Object)
            .AddSingleton(Mock.Of<IIndexNowUrlResolver>())
            .AddSingleton(client.Object)
            .AddScoped<IndexNowBatchProcessor>()
            .BuildServiceProvider();
        using var service = new IndexNowBackgroundService(queue, services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<IndexNowBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.EnqueueContent(failing);
        await Task.Delay(1500);
        Assert.False(service.ExecuteTask!.IsCompleted);

        queue.EnqueueUrl("https://www.one.nl/after/");
        for (var i = 0; i < 50 && client.Invocations.Count == 0; i++)
        {
            await Task.Delay(100);
        }

        client.Verify(item => item.SubmitAsync(It.Is<IndexNowBatch>(batch => batch.Urls.Contains("https://www.one.nl/after/")), It.IsAny<CancellationToken>()), Times.Once);
        await service.StopAsync(CancellationToken.None);
    }
}
