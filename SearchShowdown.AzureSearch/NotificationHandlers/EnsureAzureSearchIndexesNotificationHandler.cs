using Microsoft.Extensions.Options;
using SearchShowdown.AzureSearch.Services;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Models.Configuration;

namespace SearchShowdown.AzureSearch.NotificationHandlers;

internal sealed class EnsureAzureSearchIndexesNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly IAzureSearchIndexManager _indexManager;
    private readonly IndexOptions _indexOptions;

    public EnsureAzureSearchIndexesNotificationHandler(IAzureSearchIndexManager indexManager, IOptions<IndexOptions> indexOptions)
    {
        _indexManager = indexManager;
        _indexOptions = indexOptions.Value;
    }

    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        foreach (ContentIndexRegistration registration in _indexOptions.GetContentIndexRegistrations())
        {
            if (registration.Indexer == typeof(IAzureSearchIndexer))
            {
                await _indexManager.EnsureAsync(registration.IndexAlias);
            }
        }
    }
}
