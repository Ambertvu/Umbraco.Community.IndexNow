using System.Collections.Concurrent;
using System.Threading.Channels;
using IndexNow.Configuration;
using Microsoft.Extensions.Options;

namespace IndexNow.Queue;

internal sealed class IndexNowQueue(IOptions<IndexNowOptions> options) : IIndexNowQueue
{
    private readonly Channel<IndexNowWorkItem> _channel = Channel.CreateUnbounded<IndexNowWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<Guid, IReadOnlyList<IndexNowWorkItem>> _held = new();

    public ChannelReader<IndexNowWorkItem> Reader => _channel.Reader;

    public void EnqueueContent(Guid contentKey) => Enqueue(IndexNowWorkItem.ForContent(contentKey));

    public void EnqueueUrl(string absoluteUrl) => Enqueue(IndexNowWorkItem.ForUrl(absoluteUrl));

    public void Enqueue(IndexNowWorkItem item)
    {
        // Nothing reads the queue when the package is switched off.
        if (options.Value.IsActive)
        {
            _channel.Writer.TryWrite(item);
        }
    }

    /// <summary>
    /// Keeps URLs captured before a change until <see cref="Release"/>. A change can reach this more than once, and what is
    /// held already stays: a change that was cancelled left the page as it was, so that is still what the page was. Keyed by
    /// content because Umbraco does not share notification state between the two sides of every change.
    /// </summary>
    public void Hold(Guid contentKey, IReadOnlyList<IndexNowWorkItem> items)
        => _held.AddOrUpdate(contentKey, items, (_, held) => [.. held.Concat(items).DistinctBy(item => (item.Url, item.Culture))]);

    public void Release(Guid contentKey)
    {
        if (_held.TryRemove(contentKey, out IReadOnlyList<IndexNowWorkItem>? items))
        {
            foreach (IndexNowWorkItem item in items)
            {
                Enqueue(item);
            }
        }
    }
}
