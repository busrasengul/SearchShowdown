using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace SearchShowdown.Search;

public sealed class RebuildEmptyIndexesNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly ShowdownIndexService _indexService;
    private readonly ShowdownProviders _providers;
    private readonly ILogger<RebuildEmptyIndexesNotificationHandler> _logger;

    public RebuildEmptyIndexesNotificationHandler(
        ShowdownIndexService indexService,
        ShowdownProviders providers,
        ILogger<RebuildEmptyIndexesNotificationHandler> logger)
    {
        _indexService = indexService;
        _providers = providers;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        foreach (SearchProviderKind provider in _providers.Available.Where(provider => provider is not SearchProviderKind.Examine))
        {
            try
            {
                ShowdownIndexStatus status = await _indexService.StatusAsync(provider);
                if (status.DocumentCount is 0)
                {
                    _logger.LogInformation("Index {IndexAlias} is empty - rebuilding", status.IndexAlias);
                    await _indexService.RebuildAsync(provider, clearFieldCache: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not check or rebuild the {Provider} index", provider);
            }
        }
    }
}
