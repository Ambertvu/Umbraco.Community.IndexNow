using IndexNow.Configuration;
using IndexNow.Queue;
using IndexNow.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace IndexNow.Notifications;

/// <summary>
/// Queues the URLs a content change affects. URLs that are about to disappear must be read before the change, so those
/// are captured in the "-ing" notifications. Should another handler cancel the change after that, a live URL gets
/// submitted once more, which does no harm. New URLs are looked up at submit time, once the caches have caught up.
/// A failure here is logged and never gets in the way of an editor's publish.
/// </summary>
internal sealed class IndexNowContentHandler(
    IndexNowQueue queue,
    IIndexNowUrlResolver urlResolver,
    IContentService contentService,
    IOptions<IndexNowOptions> options,
    ILogger<IndexNowContentHandler> logger)
    : INotificationHandler<ContentPublishingNotification>,
        INotificationHandler<ContentPublishedNotification>,
        INotificationHandler<ContentUnpublishingNotification>,
        INotificationHandler<ContentMovingToRecycleBinNotification>,
        INotificationHandler<ContentMovingNotification>,
        INotificationHandler<ContentMovedNotification>,
        INotificationHandler<ContentDeletingNotification>
{
    private const int DescendantsPageSize = 500;

    // A rename or an unpublished culture changes the URLs, so also submit what it was before.
    public void Handle(ContentPublishingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (IContent content in notification.PublishedEntities)
            {
                CaptureCurrentUrls(content);
            }
        });

    public void Handle(ContentPublishedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (IContent content in notification.PublishedEntities)
            {
                queue.EnqueueContent(content.Key);
            }
        });

    public void Handle(ContentUnpublishingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (IContent content in notification.UnpublishedEntities)
            {
                CaptureBranch(content);
            }
        });

    public void Handle(ContentMovingToRecycleBinNotification notification)
        => Guarded(notification, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                CaptureBranch(move.Entity);
            }
        });

    public void Handle(ContentMovingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                CaptureBranch(move.Entity);
            }
        });

    public void Handle(ContentMovedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                foreach (IContent content in BranchOf(move.Entity))
                {
                    queue.EnqueueContent(content.Key);
                }
            }
        });

    public void Handle(ContentDeletingNotification notification)
        => Guarded(notification, () =>
        {
            // Emptying the recycle bin lands here too, and those pages were gone from the site already.
            foreach (IContent content in notification.DeletedEntities.Where(content => !content.Trashed))
            {
                CaptureBranch(content);
            }
        });

    private void CaptureBranch(IContent content)
    {
        foreach (IContent item in BranchOf(content))
        {
            CaptureCurrentUrls(item);
        }
    }

    private void CaptureCurrentUrls(IContent content)
    {
        foreach (ResolvedUrl url in urlResolver.Resolve(content))
        {
            queue.Enqueue(IndexNowWorkItem.ForRemovedUrl(url.Url, content, url.Culture));
        }
    }

    private IEnumerable<IContent> BranchOf(IContent content)
    {
        yield return content;

        long pageIndex = 0;
        long total;
        do
        {
            var page = contentService.GetPagedDescendants(content.Id, pageIndex++, DescendantsPageSize, out total).ToList();
            foreach (IContent descendant in page)
            {
                yield return descendant;
            }
        }
        while (pageIndex * DescendantsPageSize < total);
    }

    private void Guarded(ICancelableNotification? cancelable, Action action)
    {
        if (!options.Value.IsActive || cancelable is { Cancel: true })
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "IndexNow could not queue a content change");
        }
    }
}
