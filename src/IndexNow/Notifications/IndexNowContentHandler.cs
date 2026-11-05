using IndexNow.Configuration;
using IndexNow.Queue;
using IndexNow.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace IndexNow.Notifications;

/// <summary>
/// Queues the URLs a change affects. URLs that are about to disappear must be read before the change, in the "-ing"
/// notifications. They are held back until the "-ed" notification, which only comes once the change is saved, so that a
/// cancelled change sends nothing and a URL always enters the queue together with the change that goes with it. New URLs
/// are looked up at submit time, once the caches have caught up. A failure here is logged and never gets in the way of an
/// editor's publish.
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
        INotificationHandler<ContentUnpublishedNotification>,
        INotificationHandler<ContentMovingToRecycleBinNotification>,
        INotificationHandler<ContentMovedToRecycleBinNotification>,
        INotificationHandler<ContentMovingNotification>,
        INotificationHandler<ContentMovedNotification>,
        INotificationHandler<ContentDeletingNotification>,
        INotificationHandler<ContentDeletedNotification>,
        INotificationHandler<DomainSavingNotification>,
        INotificationHandler<DomainSavedNotification>,
        INotificationHandler<DomainDeletingNotification>,
        INotificationHandler<DomainDeletedNotification>
{
    // A rename or an unpublished culture changes the URLs, so also submit what it was before. The processor works out what
    // a rename means for the pages below it, so a publish that changes no URL costs no lookup of the branch. A culture that
    // goes offline does take the URLs below it along, and those cannot be worked out afterwards.
    public void Handle(ContentPublishingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (IContent content in notification.PublishedEntities)
            {
                var items = Capture(content).ToList();

                IReadOnlyList<string>? culturesGoingOffline = content.GetCulturesUnpublishing();
                if (culturesGoingOffline is { Count: > 0 })
                {
                    items.AddRange(contentService.GetDescendants(content.Id)
                        .SelectMany(CaptureStored)
                        .Where(item => item.Culture is not null && culturesGoingOffline.Contains(item.Culture, StringComparer.OrdinalIgnoreCase)));
                }

                items.Add(IndexNowWorkItem.ForContent(content.Key, GetCulturesPublishing(content)));
                queue.Hold(content.Key, items);
            }
        });

    // Umbraco also sends this for the pages that come back with a parent that was unpublished. Those get no Publishing
    // notification, and everything of theirs that is live came back with them.
    public void Handle(ContentPublishedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (IContent content in notification.PublishedEntities)
            {
                if (!queue.Release(content.Key).Any(item => item.ContentKey is not null))
                {
                    queue.EnqueueContent(content.Key);
                }
            }
        });

    public void Handle(ContentUnpublishingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (IContent content in notification.UnpublishedEntities)
            {
                queue.Hold(content.Key, CaptureBranch(content));
            }
        });

    public void Handle(ContentUnpublishedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (IContent content in notification.UnpublishedEntities)
            {
                queue.Release(content.Key);
            }
        });

    public void Handle(ContentMovingToRecycleBinNotification notification)
        => Guarded(notification, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                queue.Hold(move.Entity.Key, CaptureBranch(move.Entity));
            }
        });

    public void Handle(ContentMovedToRecycleBinNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                queue.Release(move.Entity.Key);
            }
        });

    // A move is a URL change like a rename, so the processor works out the pages below it the same way.
    public void Handle(ContentMovingNotification notification)
        => Guarded(notification, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                queue.Hold(move.Entity.Key, Capture(move.Entity).ToList());
            }
        });

    public void Handle(ContentMovedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (var move in notification.MoveInfoCollection)
            {
                ReleaseAndEnqueue(move.Entity.Key);
            }
        });

    public void Handle(ContentDeletingNotification notification)
        => Guarded(notification, () =>
        {
            // Emptying the recycle bin lands here too, and those pages were gone from the site already.
            foreach (IContent content in notification.DeletedEntities.Where(content => !content.Trashed))
            {
                queue.Hold(content.Key, CaptureBranch(content));
            }
        });

    public void Handle(ContentDeletedNotification notification)
        => Guarded(cancelable: null, () =>
        {
            foreach (IContent content in notification.DeletedEntities)
            {
                queue.Release(content.Key);
            }
        });

    public void Handle(DomainSavingNotification notification)
        => Guarded(notification, () => HoldSites(notification.SavedEntities));

    public void Handle(DomainSavedNotification notification)
        => Guarded(cancelable: null, () => ReleaseSites(notification.SavedEntities));

    public void Handle(DomainDeletingNotification notification)
        => Guarded(notification, () => HoldSites(notification.DeletedEntities));

    public void Handle(DomainDeletedNotification notification)
        => Guarded(cancelable: null, () => ReleaseSites(notification.DeletedEntities));

    // A domain decides the URL of everything below it. That is rare enough to look up the whole branch for.
    private void HoldSites(IEnumerable<IDomain> domains)
    {
        foreach (IContent root in RootsOf(domains))
        {
            queue.Hold(root.Key, CaptureBranch(root));
        }
    }

    private void ReleaseSites(IEnumerable<IDomain> domains)
    {
        foreach (IContent root in RootsOf(domains))
        {
            ReleaseAndEnqueue(root.Key);
        }
    }

    private IEnumerable<IContent> RootsOf(IEnumerable<IDomain> domains)
        => domains
            .Select(domain => domain.RootContentId)
            .OfType<int>()
            .Distinct()
            .Select(contentService.GetById)
            .WhereNotNull();

    /// <returns>The cultures this publish changes; <c>null</c> when it changes every culture that is live.</returns>
    private IReadOnlySet<string>? GetCulturesPublishing(IContent content)
    {
        if (!content.ContentType.VariesByCulture() || PublishesInvariantChange(content))
        {
            return null;
        }

        return content.PublishCultureInfos?.Values
            .Where(info => info.IsDirty())
            .Select(info => info.Culture)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    // An invariant value shows in every culture, whichever culture it goes live with. Umbraco has already put what goes live
    // on the entity, and the stored copy still has what is live now.
    private bool PublishesInvariantChange(IContent content)
    {
        if (!content.Published || contentService.GetById(content.Id) is not { Published: true } stored)
        {
            return false;
        }

        return content.Properties
            .Where(property => !property.PropertyType.VariesByCulture())
            .Any(property => !stored.Properties.TryGetValue(property.Alias, out IProperty? storedProperty)
                || !Equals(property.GetValue(culture: null, segment: null, published: true), storedProperty.GetValue(culture: null, segment: null, published: true)));
    }

    private void ReleaseAndEnqueue(Guid contentKey)
    {
        queue.Release(contentKey);
        queue.EnqueueContent(contentKey);
    }

    private List<IndexNowWorkItem> CaptureBranch(IContent content)
        => [.. Capture(content), .. contentService.GetDescendants(content.Id).SelectMany(CaptureStored)];

    // Umbraco changes the entity of a notification right after this, and has already cleared the published values of what
    // is being unpublished. The filters run much later and must see what the page was, so they get the stored copy.
    private IEnumerable<IndexNowWorkItem> Capture(IContent content)
    {
        IReadOnlyList<ResolvedUrl> urls = urlResolver.Resolve(content);
        if (urls.Count == 0)
        {
            return [];
        }

        IContent stored = contentService.GetById(content.Id) ?? content;
        return urls.Select(url => IndexNowWorkItem.ForRemovedUrl(url.Url, stored, url.Culture));
    }

    private IEnumerable<IndexNowWorkItem> CaptureStored(IContent stored)
        => urlResolver.Resolve(stored).Select(url => IndexNowWorkItem.ForRemovedUrl(url.Url, stored, url.Culture));

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
