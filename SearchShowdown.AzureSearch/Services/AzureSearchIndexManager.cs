using System.Collections.Concurrent;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SearchShowdown.AzureSearch.Configuration;

namespace SearchShowdown.AzureSearch.Services;

public interface IAzureSearchIndexManager
{
    Task EnsureAsync(string indexAlias);

    Task ResetAsync(string indexAlias);

    SearchClient GetSearchClient(string indexAlias);

    string IndexName(string indexAlias);
}

internal sealed class AzureSearchIndexManager : IAzureSearchIndexManager
{
    private readonly AzureSearchOptions _options;
    private readonly AzureSearchSchema _schema;
    private readonly ILogger<AzureSearchIndexManager> _logger;
    private readonly Lazy<SearchIndexClient> _indexClient;
    private readonly ConcurrentDictionary<string, SearchClient> _searchClients = new();

    public AzureSearchIndexManager(
        IOptions<AzureSearchOptions> options,
        AzureSearchSchema schema,
        ILogger<AzureSearchIndexManager> logger)
    {
        _options = options.Value;
        _schema = schema;
        _logger = logger;
        _indexClient = new Lazy<SearchIndexClient>(() => new SearchIndexClient(new Uri(_options.Endpoint), new AzureKeyCredential(_options.ApiKey)));
    }

    public string IndexName(string indexAlias)
        => $"{_options.IndexPrefix}{indexAlias}".ToLowerInvariant().Replace('_', '-');

    public SearchClient GetSearchClient(string indexAlias)
        => _searchClients.GetOrAdd(IndexName(indexAlias), name => _indexClient.Value.GetSearchClient(name));

    public async Task EnsureAsync(string indexAlias)
    {
        var name = IndexName(indexAlias);
        await _indexClient.Value.CreateOrUpdateIndexAsync(BuildIndex(name));
        _logger.LogInformation("Azure AI Search index {IndexName} is ready", name);
    }

    public async Task ResetAsync(string indexAlias)
    {
        var name = IndexName(indexAlias);
        try
        {
            await _indexClient.Value.DeleteIndexAsync(name);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        await _indexClient.Value.CreateIndexAsync(BuildIndex(name));
    }

    private SearchIndex BuildIndex(string name)
    {
        var fields = new List<SearchField>
        {
            new SimpleField(AzureFieldNames.Id, SearchFieldDataType.String) { IsKey = true },
            new SimpleField(AzureFieldNames.Key, SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField(AzureFieldNames.ObjectType, SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField(AzureFieldNames.Culture, SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField(AzureFieldNames.AccessKeys, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            new SimpleField(AzureFieldNames.PathKeys, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            new SimpleField(AzureFieldNames.ContentTypeId, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField(AzureFieldNames.Name, SearchFieldDataType.String) { IsFilterable = true, IsSortable = true },
            new SimpleField(AzureFieldNames.CreateDate, SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SimpleField(AzureFieldNames.UpdateDate, SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SearchableField(AzureFieldNames.TextsR1, collection: true),
            new SearchableField(AzureFieldNames.TextsR2, collection: true),
            new SearchableField(AzureFieldNames.TextsR3, collection: true),
            new SearchableField(AzureFieldNames.Texts, collection: true),
        };

        foreach (AzureSearchFieldOptions.Field field in _schema.DeclaredFields)
        {
            fields.AddRange(BuildFields(field));
        }

        return new SearchIndex(name, fields)
        {
            ScoringProfiles =
            {
                new ScoringProfile(AzureFieldNames.ScoringProfile)
                {
                    TextWeights = new TextWeights(new Dictionary<string, double>
                    {
                        [AzureFieldNames.TextsR1] = 6,
                        [AzureFieldNames.TextsR2] = 4,
                        [AzureFieldNames.TextsR3] = 2,
                        [AzureFieldNames.Texts] = 1,
                    }),
                },
            },
            DefaultScoringProfile = AzureFieldNames.ScoringProfile,
        };
    }

    private static IEnumerable<SearchField> BuildFields(AzureSearchFieldOptions.Field field)
    {
        var name = AzureFieldNames.ForProperty(field);

        if (field.FieldValues is AzureFieldValues.Texts)
        {
            yield return new SearchableField(name, collection: true);
            yield break;
        }

        if (field.FieldValues is AzureFieldValues.Keywords)
        {
            yield return new SimpleField(name, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = field.Facetable };
            if (field.Sortable)
            {
                yield return new SimpleField(AzureFieldNames.SortForKeyword(field), SearchFieldDataType.String) { IsSortable = true };
            }

            yield break;
        }

        SearchFieldDataType type = field.FieldValues switch
        {
            AzureFieldValues.Integers => SearchFieldDataType.Int32,
            AzureFieldValues.Decimals => SearchFieldDataType.Double,
            AzureFieldValues.DateTimeOffsets => SearchFieldDataType.DateTimeOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        yield return field.Sortable
            ? new SimpleField(name, type) { IsFilterable = true, IsFacetable = field.Facetable, IsSortable = true }
            : new SimpleField(name, SearchFieldDataType.Collection(type)) { IsFilterable = true, IsFacetable = field.Facetable };
    }
}
