using System.Diagnostics;
using System.Threading.Channels;

namespace IndexNow.Queue;

/// <summary>
/// Waits until the queue has been quiet for the delay, so every item has had that long for Umbraco's caches to catch up.
/// The maximum keeps a steady stream of publishes from holding back submissions for ever.
/// </summary>
internal static class IndexNowBatchCollector
{
    public static async Task<List<IndexNowWorkItem>> CollectAsync(
        ChannelReader<IndexNowWorkItem> reader,
        TimeSpan delay,
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        var items = new List<IndexNowWorkItem>();
        var elapsed = Stopwatch.StartNew();

        Drain(reader, items);
        while (true)
        {
            await Task.Delay(delay, cancellationToken);

            // Items that came in during this wait stay in the queue when this returns: they have not waited long enough.
            if (!reader.TryPeek(out _) || elapsed.Elapsed >= maxWait)
            {
                return items;
            }

            Drain(reader, items);
        }
    }

    private static void Drain(ChannelReader<IndexNowWorkItem> reader, List<IndexNowWorkItem> items)
    {
        while (reader.TryRead(out IndexNowWorkItem? item))
        {
            items.Add(item);
        }
    }
}
