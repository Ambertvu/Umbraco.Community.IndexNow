using IndexNow.Configuration;
using IndexNow.Filtering;
using IndexNow.KeyFile;
using IndexNow.Notifications;
using IndexNow.Queue;
using IndexNow.Submission;
using IndexNow.Urls;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace IndexNow.Composers;

public sealed class IndexNowComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<IndexNowOptions>().Bind(builder.Config.GetSection(IndexNowOptions.SectionName));

        builder.Services.AddSingleton<IndexNowQueue>();
        builder.Services.AddSingleton<IIndexNowQueue>(services => services.GetRequiredService<IndexNowQueue>());
        builder.Services.AddSingleton<IIndexNowUrlResolver, IndexNowUrlResolver>();
        builder.Services.AddScoped<IndexNowBatchProcessor>();
        builder.AddIndexNowUrlFilter<DefaultIndexNowUrlFilter>();
        builder.Services.AddHttpClient<IIndexNowClient, IndexNowClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddHostedService<IndexNowBackgroundService>();
        builder.Services.AddSingleton<IStartupFilter, IndexNowKeyFileStartupFilter>();

        builder
            .AddNotificationHandler<ContentPublishingNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentPublishedNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentUnpublishingNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentMovingToRecycleBinNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentMovingNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentMovedNotification, IndexNowContentHandler>()
            .AddNotificationHandler<ContentDeletingNotification, IndexNowContentHandler>();
    }
}
