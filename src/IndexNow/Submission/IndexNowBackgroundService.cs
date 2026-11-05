using IndexNow.Configuration;
using IndexNow.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndexNow.Submission;

/// <summary>
/// Collects changes for a moment and submits them together. The queue lives in memory: changes still waiting during a
/// restart are lost, and search engines pick those up from the sitemap as they always did.
/// </summary>
internal sealed class IndexNowBackgroundService(
    IndexNowQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<IndexNowOptions> options,
    ILogger<IndexNowBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IndexNowOptions settings = options.Value;
        if (!settings.IsActive)
        {
            if (settings.Enabled && !string.IsNullOrEmpty(settings.Key))
            {
                logger.LogWarning("IndexNow is off: the key must be 8-128 letters, digits or hyphens");
            }

            return;
        }

        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(settings.BatchDelay, stoppingToken);

                var items = new List<IndexNowWorkItem>();
                while (queue.Reader.TryRead(out IndexNowWorkItem? item))
                {
                    items.Add(item);
                }

                await ProcessAsync(items, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task ProcessAsync(IReadOnlyCollection<IndexNowWorkItem> items, CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IndexNowBatchProcessor>().ProcessAsync(items, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "IndexNow could not process {Count} changes", items.Count);
        }
    }
}
