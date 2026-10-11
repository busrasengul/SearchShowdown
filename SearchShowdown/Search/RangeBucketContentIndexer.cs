using Kjac.SearchProvider.Algolia.Helpers;
using SearchShowdown.Seeding;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;

namespace SearchShowdown.Search;

public sealed class RangeBucketContentIndexer : IContentIndexer
{
    private static readonly IRangeFacetDefinition[] RangeFacets = ShowdownFacets.All.OfType<IRangeFacetDefinition>().ToArray();

    public Task<IEnumerable<IndexField>> GetIndexFieldsAsync(
        IContentBase content,
        string?[] cultures,
        bool published,
        CancellationToken cancellationToken)
    {
        if (content.ContentType.Alias is not (DemoContentManager.ArticleAlias or DemoContentManager.ProductAlias))
        {
            return Task.FromResult<IEnumerable<IndexField>>([]);
        }

        IndexField[] fields = RangeFacets
            .Where(definition => content.HasProperty(definition.Field))
            .Select(definition => (definition.Field, Bucket: definition.BucketFor(content.GetValue(definition.Field, published: published))))
            .Where(bucket => bucket.Bucket is not null)
            .Select(bucket => RangeIndexFieldHelper.Create(bucket.Field, bucket.Bucket!))
            .ToArray();

        return Task.FromResult<IEnumerable<IndexField>>(fields);
    }
}
