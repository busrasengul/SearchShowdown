using Microsoft.Extensions.DependencyInjection;
using SearchShowdown.AzureSearch.Configuration;
using SearchShowdown.AzureSearch.NotificationHandlers;
using SearchShowdown.AzureSearch.Services;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;

namespace SearchShowdown.AzureSearch.DependencyInjection;

public static class UmbracoBuilderExtensions
{
    public static IUmbracoBuilder AddAzureSearchProvider(this IUmbracoBuilder builder)
    {
        builder.Services.Configure<AzureSearchOptions>(builder.Config.GetSection(AzureSearchOptions.SectionName));
        builder.Services.AddSingleton<AzureSearchSchema>();
        builder.Services.AddSingleton<IAzureSearchIndexManager, AzureSearchIndexManager>();
        builder.Services.AddSingleton<AzureSearchBatchWriter>();
        builder.Services.AddTransient<IAzureSearchIndexer, AzureSearchIndexer>();
        builder.Services.AddTransient<IAzureSearchSearcher, AzureSearchSearcher>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, EnsureAzureSearchIndexesNotificationHandler>();
        return builder;
    }

    public static IndexOptions RegisterAzureSearchContentIndex<TContentChangeStrategy>(
        this IndexOptions indexOptions,
        string indexAlias,
        params UmbracoObjectTypes[] containedObjectTypes)
        where TContentChangeStrategy : class, IContentChangeStrategy
    {
        indexOptions.RegisterContentIndex<IAzureSearchIndexer, IAzureSearchSearcher, TContentChangeStrategy>(
            indexAlias,
            sameOriginOnly: true,
            containedObjectTypes);
        return indexOptions;
    }
}
