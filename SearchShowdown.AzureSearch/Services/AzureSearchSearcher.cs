using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SearchShowdown.AzureSearch.Configuration;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;
using AzureFacetResult = Azure.Search.Documents.Models.FacetResult;
using CoreFacetResult = Umbraco.Cms.Search.Core.Models.Searching.Faceting.FacetResult;
using CoreSearchResult = Umbraco.Cms.Search.Core.Models.Searching.SearchResult;

namespace SearchShowdown.AzureSearch.Services;

public interface IAzureSearchSearcher : ISearcher
{
}

internal sealed class AzureSearchSearcher : IAzureSearchSearcher
{
    private const string LuceneSpecialCharacters = "+-&|!(){}[]^\"~*?:\\/";

    private readonly IAzureSearchIndexManager _indexManager;
    private readonly AzureSearchSchema _schema;
    private readonly AzureSearchOptions _options;
    private readonly ILogger<AzureSearchSearcher> _logger;

    public AzureSearchSearcher(
        IAzureSearchIndexManager indexManager,
        AzureSearchSchema schema,
        IOptions<AzureSearchOptions> options,
        ILogger<AzureSearchSearcher> logger)
    {
        _indexManager = indexManager;
        _schema = schema;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CoreSearchResult> SearchAsync(
        string indexAlias,
        string? query = null,
        IEnumerable<Filter>? filters = null,
        IEnumerable<Facet>? facets = null,
        IEnumerable<Sorter>? sorters = null,
        string? culture = null,
        string? segment = null,
        AccessContext? accessContext = null,
        int skip = 0,
        int take = 10,
        int maxSuggestions = 0)
    {
        Filter[] filtersArray = filters?.ToArray() ?? [];
        Facet[] facetsArray = facets?.ToArray() ?? [];
        SearchClient client = _indexManager.GetSearchClient(indexAlias);
        var searchText = SearchText(query);
        List<string> baseFilters = BaseFilters(culture, accessContext);

        SearchOptions mainOptions = CreateOptions(baseFilters, filtersArray, facetsArray);
        mainOptions.IncludeTotalCount = true;
        mainOptions.Skip = skip;
        mainOptions.Size = take;
        mainOptions.Select.Add(AzureFieldNames.Key);
        mainOptions.Select.Add(AzureFieldNames.ObjectType);
        foreach (var orderBy in OrderBy(sorters))
        {
            mainOptions.OrderBy.Add(orderBy);
        }

        Task<Response<SearchResults<SearchDocument>>> mainSearch = client.SearchAsync<SearchDocument>(searchText, mainOptions);

        var activeFilterFields = filtersArray.Select(filter => filter.FieldName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        (Facet Facet, Task<Response<SearchResults<SearchDocument>>> Search)[] expandedFacetSearches = facetsArray
            .Where(facet => activeFilterFields.Contains(facet.FieldName))
            .Select(facet =>
            {
                Filter[] otherFilters = filtersArray
                    .Where(filter => string.Equals(filter.FieldName, facet.FieldName, StringComparison.OrdinalIgnoreCase) is false)
                    .ToArray();
                SearchOptions facetOptions = CreateOptions(baseFilters, otherFilters, [facet]);
                facetOptions.Size = 0;
                return (facet, client.SearchAsync<SearchDocument>(searchText, facetOptions));
            })
            .ToArray();

        await Task.WhenAll(expandedFacetSearches.Select(expanded => (Task)expanded.Search).Append(mainSearch));

        SearchResults<SearchDocument> mainResults = mainSearch.Result.Value;
        var azureFacets = new Dictionary<string, IList<AzureFacetResult>>(
            mainResults.Facets ?? new Dictionary<string, IList<AzureFacetResult>>(),
            StringComparer.OrdinalIgnoreCase);

        foreach ((Facet facet, Task<Response<SearchResults<SearchDocument>>> search) in expandedFacetSearches)
        {
            AzureField? field = _schema.Resolve(facet.FieldName);
            if (field is not null && search.Result.Value.Facets?.TryGetValue(field.Name, out IList<AzureFacetResult>? values) is true)
            {
                azureFacets[field.Name] = values;
            }
        }

        CoreFacetResult[] facetResults = facetsArray
            .Select(facet => ToFacetResult(facet, azureFacets))
            .OfType<CoreFacetResult>()
            .ToArray();

        var documents = new List<Document>();
        foreach (SearchResult<SearchDocument> hit in mainResults.GetResults())
        {
            if (Guid.TryParse(hit.Document.GetString(AzureFieldNames.Key), out Guid key))
            {
                documents.Add(new Document(
                    key,
                    Enum.TryParse(hit.Document.GetString(AzureFieldNames.ObjectType), out UmbracoObjectTypes objectType)
                        ? objectType
                        : UmbracoObjectTypes.Unknown));
            }
        }

        return new CoreSearchResult(mainResults.TotalCount ?? 0, documents, facetResults);
    }

    private SearchOptions CreateOptions(IEnumerable<string> baseFilters, IEnumerable<Filter> filters, IEnumerable<Facet> facets)
    {
        var options = new SearchOptions
        {
            SearchMode = SearchMode.All,
            QueryType = _options.FuzzySearch ? SearchQueryType.Full : SearchQueryType.Simple,
        };

        foreach (var searchField in AzureFieldNames.SearchFields)
        {
            options.SearchFields.Add(searchField);
        }

        options.Filter = string.Join(" and ", baseFilters.Concat(filters.Select(FilterExpression).OfType<string>()));

        foreach (var facetExpression in facets.Select(FacetExpression).OfType<string>())
        {
            options.Facets.Add(facetExpression);
        }

        return options;
    }

    private string SearchText(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "*";
        }

        if (_options.FuzzySearch is false)
        {
            return query;
        }

        IEnumerable<string> terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(EscapeLucene)
            .Select(term => term.Length >= 4 ? $"{term}~1" : term);

        return string.Join(' ', terms);
    }

    private static List<string> BaseFilters(string? culture, AccessContext? accessContext)
    {
        var filters = new List<string>
        {
            culture is null
                ? $"{AzureFieldNames.Culture} eq '{AzureFieldNames.InvariantCulture}'"
                : $"({AzureFieldNames.Culture} eq '{AzureFieldNames.InvariantCulture}' or {AzureFieldNames.Culture} eq '{Escape(culture.ToLowerInvariant())}')",
        };

        if (accessContext?.Bypass is true)
        {
            return filters;
        }

        if (accessContext is null)
        {
            filters.Add($"not {AzureFieldNames.AccessKeys}/any()");
            return filters;
        }

        var principals = string.Join(',', new[] { accessContext.PrincipalId }.Concat(accessContext.GroupIds ?? []).Select(id => id.ToString("D")));
        filters.Add($"(not {AzureFieldNames.AccessKeys}/any() or {AzureFieldNames.AccessKeys}/any(a: search.in(a, '{principals}', ',')))");
        return filters;
    }

    private string? FilterExpression(Filter filter)
    {
        AzureField? field = _schema.Resolve(filter.FieldName);
        if (field is null)
        {
            _logger.LogWarning("Filter field {FieldName} is not declared for Azure AI Search and will be ignored", filter.FieldName);
            return null;
        }

        var expression = filter switch
        {
            KeywordFilter keywordFilter => StringIn(field, keywordFilter.Values),
            TextFilter textFilter => $"search.ismatch('{Escape(string.Join(' ', textFilter.Values))}', '{field.Name}')",
            IntegerExactFilter exact => AnyOf(field, exact.Values.Select(value => Equal(Number(value)))),
            DecimalExactFilter exact => AnyOf(field, exact.Values.Select(value => Equal(Number(value)))),
            DateTimeOffsetExactFilter exact => AnyOf(field, exact.Values.Select(value => Equal(Date(value)))),
            IntegerRangeFilter range => AnyOf(field, range.Ranges.Select(r => Between(Number(r.MinValue), Number(r.MaxValue)))),
            DecimalRangeFilter range => AnyOf(field, range.Ranges.Select(r => Between(Number(r.MinValue), Number(r.MaxValue)))),
            DateTimeOffsetRangeFilter range => AnyOf(field, range.Ranges.Select(r => Between(Date(r.MinValue), Date(r.MaxValue)))),
            _ => null,
        };

        if (expression is null)
        {
            return null;
        }

        return filter.Negate ? $"not ({expression})" : $"({expression})";
    }

    private static string StringIn(AzureField field, IEnumerable<string> values)
    {
        var list = Escape(string.Join('|', values));
        return field.IsCollection
            ? $"{field.Name}/any(x: search.in(x, '{list}', '|'))"
            : $"search.in({field.Name}, '{list}', '|')";
    }

    private static string? AnyOf(AzureField field, IEnumerable<Func<string, string>> predicates)
    {
        var variable = field.IsCollection ? "x" : field.Name;
        var parts = predicates.Select(predicate => $"({predicate(variable)})").ToArray();
        if (parts.Length is 0)
        {
            return null;
        }

        var body = string.Join(" or ", parts);
        return field.IsCollection ? $"{field.Name}/any(x: {body})" : body;
    }

    private static Func<string, string> Equal(string? value)
        => variable => $"{variable} eq {value}";

    private static Func<string, string> Between(string? min, string? max)
        => variable => (min, max) switch
        {
            (null, null) => "true",
            (not null, null) => $"{variable} ge {min}",
            (null, not null) => $"{variable} lt {max}",
            _ => $"{variable} ge {min} and {variable} lt {max}",
        };

    private string? FacetExpression(Facet facet)
    {
        AzureField? field = _schema.Resolve(facet.FieldName);
        if (field is null)
        {
            _logger.LogWarning("Facet field {FieldName} is not declared for Azure AI Search and will be ignored", facet.FieldName);
            return null;
        }

        var boundaries = facet switch
        {
            IntegerRangeFacet range => Boundaries(range.Ranges.SelectMany(r => new[] { r.MinValue, r.MaxValue }), value => Number(value)!),
            DecimalRangeFacet range => Boundaries(range.Ranges.SelectMany(r => new[] { r.MinValue, r.MaxValue }), value => Number(value)!),
            DateTimeOffsetRangeFacet range => Boundaries(range.Ranges.SelectMany(r => new[] { r.MinValue, r.MaxValue }), value => Date(value)!),
            _ => null,
        };

        return boundaries is { Length: > 0 }
            ? $"{field.Name},values:{string.Join('|', boundaries)}"
            : $"{field.Name},count:{_options.MaxFacetValues}";
    }

    private static string[] Boundaries<T>(IEnumerable<T?> values, Func<T, string> format)
        where T : struct
        => values.OfType<T>().Distinct().Order().Select(format).ToArray();

    private CoreFacetResult? ToFacetResult(Facet facet, IReadOnlyDictionary<string, IList<AzureFacetResult>> azureFacets)
    {
        AzureField? field = _schema.Resolve(facet.FieldName);
        if (field is null)
        {
            return null;
        }

        IList<AzureFacetResult> buckets = azureFacets.TryGetValue(field.Name, out IList<AzureFacetResult>? values) ? values : [];

        IEnumerable<FacetValue> facetValues = facet switch
        {
            KeywordFacet => buckets.Select(b => new KeywordFacetValue(Convert.ToString(Value(b, "value"), CultureInfo.InvariantCulture) ?? string.Empty, b.Count ?? 0)),
            IntegerExactFacet => buckets.Select(b => new IntegerExactFacetValue((int)(ToDouble(Value(b, "value")) ?? 0), b.Count ?? 0)),
            DecimalExactFacet => buckets.Select(b => new DecimalExactFacetValue((decimal)(ToDouble(Value(b, "value")) ?? 0), b.Count ?? 0)),
            DateTimeOffsetExactFacet => buckets.Select(b => new DateTimeOffsetExactFacetValue(ToDate(Value(b, "value")) ?? DateTimeOffset.MinValue, b.Count ?? 0)),
            IntegerRangeFacet range => range.Ranges.Select(r => new IntegerRangeFacetValue(r.Key, r.MinValue, r.MaxValue, SumBuckets(buckets, r.MinValue, r.MaxValue))),
            DecimalRangeFacet range => range.Ranges.Select(r => new DecimalRangeFacetValue(r.Key, r.MinValue, r.MaxValue, SumBuckets(buckets, (double?)r.MinValue, (double?)r.MaxValue))),
            DateTimeOffsetRangeFacet range => range.Ranges.Select(r => new DateTimeOffsetRangeFacetValue(r.Key, r.MinValue, r.MaxValue, SumBuckets(buckets, r.MinValue?.UtcTicks, r.MaxValue?.UtcTicks))),
            _ => [],
        };

        return new CoreFacetResult(facet.FieldName, facetValues.ToArray());
    }

    private static long SumBuckets(IEnumerable<AzureFacetResult> buckets, double? min, double? max)
    {
        long total = 0;
        foreach (AzureFacetResult bucket in buckets)
        {
            var from = ToDouble(Value(bucket, "from"));
            var to = ToDouble(Value(bucket, "to"));
            var withinLower = min is null || (from is not null && from >= min);
            var withinUpper = max is null || (to is not null && to <= max);
            if (withinLower && withinUpper)
            {
                total += bucket.Count ?? 0;
            }
        }

        return total;
    }

    private IEnumerable<string> OrderBy(IEnumerable<Sorter>? sorters)
    {
        foreach (Sorter sorter in sorters ?? [])
        {
            var direction = sorter.Direction is Direction.Ascending ? "asc" : "desc";
            if (sorter is ScoreSorter)
            {
                yield return $"search.score() {direction}";
                continue;
            }

            AzureField? field = _schema.Resolve(sorter.FieldName);
            if (field is not { Sortable: true })
            {
                _logger.LogWarning("Sort field {FieldName} is not sortable in Azure AI Search and will be ignored", sorter.FieldName);
                continue;
            }

            yield return $"{field.SortName} {direction}";
        }
    }

    private static object? Value(AzureFacetResult bucket, string key)
        => bucket.TryGetValue(key, out var value) ? value : null;

    private static double? ToDouble(object? value)
        => value switch
        {
            null => null,
            long l => l,
            int i => i,
            double d => d,
            float f => f,
            decimal m => (double)m,
            DateTimeOffset dto => dto.UtcTicks,
            DateTime dt => new DateTimeOffset(dt.ToUniversalTime()).UtcTicks,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } element => ToDouble(element.GetString()),
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedDate) => parsedDate.UtcTicks,
            _ => null,
        };

    private static DateTimeOffset? ToDate(object? value)
        => ToDouble(value) is { } ticks ? new DateTimeOffset((long)ticks, TimeSpan.Zero) : null;

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Date(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.Replace("'", "''");

    private static string EscapeLucene(string term)
    {
        var builder = new StringBuilder(term.Length);
        foreach (var character in term)
        {
            if (LuceneSpecialCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
