using System.Threading.Channels;
using IndexNow.Queue;

namespace IndexNow.Tests;

public class BatchCollectorTests
{
    private static readonly TimeSpan _delay = TimeSpan.FromMilliseconds(300);

    private readonly Channel<IndexNowWorkItem> _channel = Channel.CreateUnbounded<IndexNowWorkItem>(new UnboundedChannelOptions { SingleReader = true });

    [Fact]
    public async Task Keeps_waiting_while_items_come_in()
    {
        Write("https://www.one.nl/a/");
        Task<List<IndexNowWorkItem>> batch = IndexNowBatchCollector.CollectAsync(_channel.Reader, _delay, _delay * 20, CancellationToken.None);

        await Task.Delay(_delay / 2);
        Write("https://www.one.nl/b/");

        Assert.Equal(2, (await batch).Count);
    }

    [Fact]
    public async Task Every_returned_item_has_waited_the_delay()
    {
        Write("https://www.one.nl/a/");
        var started = DateTime.UtcNow;

        await IndexNowBatchCollector.CollectAsync(_channel.Reader, _delay, _delay * 20, CancellationToken.None);

        Assert.True(DateTime.UtcNow - started >= _delay * 0.9);
    }

    [Fact]
    public async Task Stops_at_the_maximum_and_leaves_items_that_have_not_waited_long_enough()
    {
        using var stop = new CancellationTokenSource();
        Write("https://www.one.nl/first/");
        var writer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                Write("https://www.one.nl/next/");
                await Task.Delay(_delay / 6);
            }
        });

        List<IndexNowWorkItem> batch = await IndexNowBatchCollector.CollectAsync(_channel.Reader, _delay, _delay * 2, CancellationToken.None);
        await stop.CancelAsync();
        await writer;

        Assert.NotEmpty(batch);
        Assert.True(_channel.Reader.TryPeek(out _));
    }

    private void Write(string url) => _channel.Writer.TryWrite(IndexNowWorkItem.ForUrl(url));
}
