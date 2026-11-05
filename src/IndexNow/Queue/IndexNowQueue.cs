using System.Threading.Channels;
using IndexNow.Configuration;
using Microsoft.Extensions.Options;

namespace IndexNow.Queue;

internal sealed class IndexNowQueue(IOptions<IndexNowOptions> options) : IIndexNowQueue
{
    private readonly Channel<IndexNowWorkItem> _channel = Channel.CreateUnbounded<IndexNowWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

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
}
