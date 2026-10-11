using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;

namespace SearchShowdown.Search;

public sealed record ShowdownIndexStatus(
    SearchProviderKind Provider,
    string IndexAlias,
    long DocumentCount,
    string HealthStatus,
    string ProviderName);

public sealed class ShowdownIndexService
{
    private readonly IIndexerResolver _indexerResolver;
    private readonly IDistributedContentIndexRebuilder _rebuilder;
    private readonly IIndexDocumentService _indexDocumentService;
    private readonly ShowdownProviders _providers;

    public ShowdownIndexService(
        IIndexerResolver indexerResolver,
        IDistributedContentIndexRebuilder rebuilder,
        IIndexDocumentService indexDocumentService,
        ShowdownProviders providers)
    {
        _indexerResolver = indexerResolver;
        _rebuilder = rebuilder;
        _indexDocumentService = indexDocumentService;
        _providers = providers;
    }

    public async Task<ShowdownIndexStatus> StatusAsync(SearchProviderKind provider)
    {
        var indexAlias = ShowdownIndexes.For(provider);
        IndexMetadata metadata = await _indexerResolver.GetRequiredIndexer(indexAlias).GetMetadataAsync(indexAlias);
        return new ShowdownIndexStatus(provider, indexAlias, metadata.DocumentCount, metadata.HealthStatus.ToString(), metadata.ProviderName);
    }

    public async Task<ShowdownIndexStatus[]> StatusAsync()
        => await Task.WhenAll(_providers.Available.Select(StatusAsync));

    public async Task<bool> RebuildAsync(SearchProviderKind provider, bool clearFieldCache)
    {
        if (clearFieldCache)
        {
            await _indexDocumentService.DeleteAllAsync();
        }

        return _rebuilder.Rebuild(ShowdownIndexes.For(provider));
    }
}
