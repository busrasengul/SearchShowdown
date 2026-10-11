using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using SearchShowdown.AzureSearch.Configuration;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services;
using CoreFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;

namespace SearchShowdown.AzureSearch.Services;

public interface IAzureSearchIndexer : IIndexer
{
}

internal sealed class AzureSearchIndexer : IAzureSearchIndexer
{
    private const int BatchSize = 1000;

    private readonly IAzureSearchIndexManager _indexManager;
    private readonly AzureSearchSchema _schema;
    private readonly AzureSearchBatchWriter _batchWriter;

    public AzureSearchIndexer(IAzureSearchIndexManager indexManager, AzureSearchSchema schema, AzureSearchBatchWriter batchWriter)
    {
        _indexManager = indexManager;
        _schema = schema;
        _batchWriter = batchWriter;
    }

    public async Task AddOrUpdateAsync(
        string indexAlias,
        Guid id,
        UmbracoObjectTypes objectType,
        IEnumerable<Variation> variations,
        IEnumerable<IndexField> fields,
        ContentProtection? protection)
    {
        IndexField[] fieldsArray = fields as IndexField[] ?? fields.ToArray();
        var accessKeys = protection?.AccessIds.Select(accessId => accessId.ToString("D")).ToArray() ?? [];

        SearchDocument[] documents = variations
            .Where(variation => variation.Segment is null)
            .Select(variation => variation.Culture)
            .Distinct()
            .Select(culture => BuildDocument(id, objectType, culture, fieldsArray, accessKeys))
            .ToArray();

        if (documents.Length is 0)
        {
            return;
        }

        await _batchWriter.EnqueueAsync(indexAlias, documents);
    }

    public async Task DeleteAsync(string indexAlias, IEnumerable<Guid> ids)
    {
        var keys = ids.Select(id => id.ToString("D")).ToArray();
        if (keys.Length is 0)
        {
            return;
        }

        await _batchWriter.FlushAsync(indexAlias);

        SearchClient client = _indexManager.GetSearchClient(indexAlias);
        var keyList = string.Join(',', keys);
        var filter = $"search.in({AzureFieldNames.Key}, '{keyList}', ',') or {AzureFieldNames.PathKeys}/any(p: search.in(p, '{keyList}', ','))";

        var documentIds = new List<string>();
        while (true)
        {
            var options = new SearchOptions { Filter = filter, Size = BatchSize, Skip = documentIds.Count };
            options.Select.Add(AzureFieldNames.Id);

            Response<SearchResults<SearchDocument>> response = await client.SearchAsync<SearchDocument>("*", options);
            var page = new List<string>();
            await foreach (SearchResult<SearchDocument> result in response.Value.GetResultsAsync())
            {
                page.Add(result.Document.GetString(AzureFieldNames.Id));
            }

            documentIds.AddRange(page);
            if (page.Count < BatchSize)
            {
                break;
            }
        }

        foreach (string[] chunk in documentIds.Chunk(BatchSize))
        {
            await client.DeleteDocumentsAsync(AzureFieldNames.Id, chunk);
        }
    }

    public async Task ResetAsync(string indexAlias)
    {
        _batchWriter.Discard(indexAlias);
        await _indexManager.ResetAsync(indexAlias);
    }

    public async Task<IndexMetadata> GetMetadataAsync(string indexAlias)
    {
        try
        {
            Response<long> count = await _indexManager.GetSearchClient(indexAlias).GetDocumentCountAsync();
            return new IndexMetadata(count.Value, count.Value > 0 ? HealthStatus.Healthy : HealthStatus.Empty, "Azure AI Search");
        }
        catch (RequestFailedException)
        {
            return new IndexMetadata(0, HealthStatus.Unknown, "Azure AI Search");
        }
    }

    private SearchDocument BuildDocument(Guid id, UmbracoObjectTypes objectType, string? culture, IndexField[] fields, string[] accessKeys)
    {
        var document = new SearchDocument
        {
            [AzureFieldNames.Id] = $"{id:N}-{culture?.ToLowerInvariant() ?? AzureFieldNames.InvariantCulture}",
            [AzureFieldNames.Key] = id.ToString("D"),
            [AzureFieldNames.ObjectType] = objectType.ToString(),
            [AzureFieldNames.Culture] = culture?.ToLowerInvariant() ?? AzureFieldNames.InvariantCulture,
            [AzureFieldNames.AccessKeys] = accessKeys,
        };

        var textsR1 = new List<string>();
        var textsR2 = new List<string>();
        var textsR3 = new List<string>();
        var texts = new List<string>();

        IEnumerable<IndexField> applicableFields = fields.Where(field =>
            field.Segment is null
            && (field.Culture is null || string.Equals(field.Culture, culture, StringComparison.OrdinalIgnoreCase)));

        foreach (IndexField field in applicableFields)
        {
            IndexValue value = field.Value;
            textsR1.AddRange(value.TextsR1 ?? []);
            textsR2.AddRange(value.TextsR2 ?? []);
            textsR3.AddRange(value.TextsR3 ?? []);
            texts.AddRange(value.Texts ?? []);

            switch (field.FieldName)
            {
                case CoreFieldNames.PathIds:
                    document[AzureFieldNames.PathKeys] = value.Keywords?.ToArray() ?? [];
                    break;
                case CoreFieldNames.ContentTypeId:
                    document[AzureFieldNames.ContentTypeId] = value.Keywords?.FirstOrDefault();
                    break;
                case CoreFieldNames.Name:
                    document[AzureFieldNames.Name] = (value.TextsR1 ?? value.Texts ?? value.Keywords)?.FirstOrDefault();
                    break;
                case CoreFieldNames.CreateDate:
                    document[AzureFieldNames.CreateDate] = value.DateTimeOffsets?.FirstOrDefault();
                    break;
                case CoreFieldNames.UpdateDate:
                    document[AzureFieldNames.UpdateDate] = value.DateTimeOffsets?.FirstOrDefault();
                    break;
                default:
                    if (_schema.TryGetDeclaredField(field.FieldName, out AzureSearchFieldOptions.Field declaredField))
                    {
                        AddDeclaredValue(document, declaredField, value);
                    }

                    break;
            }
        }

        document[AzureFieldNames.TextsR1] = textsR1;
        document[AzureFieldNames.TextsR2] = textsR2;
        document[AzureFieldNames.TextsR3] = textsR3;
        document[AzureFieldNames.Texts] = texts;

        return document;
    }

    private static void AddDeclaredValue(SearchDocument document, AzureSearchFieldOptions.Field field, IndexValue value)
    {
        var name = AzureFieldNames.ForProperty(field);

        switch (field.FieldValues)
        {
            case AzureFieldValues.Keywords:
                var keywords = value.Keywords?.ToArray() ?? [];
                document[name] = keywords;
                if (field.Sortable)
                {
                    document[AzureFieldNames.SortForKeyword(field)] = keywords.FirstOrDefault();
                }

                break;
            case AzureFieldValues.Integers:
                SetNumeric(document, name, field.Sortable, value.Integers?.ToArray() ?? []);
                break;
            case AzureFieldValues.Decimals:
                SetNumeric(document, name, field.Sortable, value.Decimals?.Select(d => (double)d).ToArray() ?? []);
                break;
            case AzureFieldValues.DateTimeOffsets:
                SetNumeric(document, name, field.Sortable, value.DateTimeOffsets?.ToArray() ?? []);
                break;
            case AzureFieldValues.Texts:
                document[name] = (value.Texts ?? [])
                    .Concat(value.TextsR1 ?? [])
                    .Concat(value.TextsR2 ?? [])
                    .Concat(value.TextsR3 ?? [])
                    .ToArray();
                break;
        }
    }

    private static void SetNumeric<T>(SearchDocument document, string name, bool single, T[] values)
        where T : struct
        => document[name] = single
            ? values.Length > 0 ? values[0] : null
            : values;
}
