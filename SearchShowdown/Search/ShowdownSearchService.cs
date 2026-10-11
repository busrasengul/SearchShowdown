using System.Diagnostics;
using SearchShowdown.Seeding;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Search.Core.Extensions;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Extensions;
using CoreFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;

namespace SearchShowdown.Search;

public sealed class ShowdownSearchRequest
{
    public string? Q { get; set; }

    public SearchProviderKind Provider { get; set; } = SearchProviderKind.Examine;

    public string? Type { get; set; }

    public string? Sort { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;

    public bool Compare { get; set; }

    public bool WithoutFacets { get; set; }

    public Dictionary<string, string[]> Selected { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string[] SelectedFor(string field) => Selected.TryGetValue(field, out var values) ? values : [];

    public int ActiveFacetCount => Selected.Count(selection => selection.Value.Length > 0);

    public static ShowdownSearchRequest FromQuery(IQueryCollection query)
    {
        var request = new ShowdownSearchRequest
        {
            Q = query["q"].FirstOrDefault(),
            Type = query["type"].FirstOrDefault(),
            Sort = query["sort"].FirstOrDefault(),
            Page = int.TryParse(query["page"].FirstOrDefault(), out var page) ? page : 1,
            PageSize = int.TryParse(query["pageSize"].FirstOrDefault(), out var pageSize) ? pageSize : 12,
            Compare = bool.TryParse(query["compare"].FirstOrDefault(), out var compare) && compare,
            WithoutFacets = string.Equals(query["facets"].FirstOrDefault(), "none", StringComparison.OrdinalIgnoreCase),
            Provider = Enum.TryParse(query["provider"].FirstOrDefault(), true, out SearchProviderKind provider)
                ? provider
                : SearchProviderKind.Examine,
        };

        foreach (ShowdownFacetDefinition definition in ShowdownFacets.All)
        {
            var values = query[definition.Field].Where(value => string.IsNullOrWhiteSpace(value) is false).Select(value => value!).ToArray();
            if (values.Length > 0)
            {
                request.Selected[definition.Field] = values;
            }
        }

        return request;
    }
}

public sealed record ShowdownFacetValue(string Key, string Label, long Count);

public sealed record ShowdownFacet(string Field, string Title, IReadOnlyList<ShowdownFacetValue> Values);

public sealed record ShowdownHit(
    Guid Key,
    string Name,
    string ContentType,
    string? Summary,
    string? Category,
    string? Audience,
    string? Topic,
    bool Featured,
    int? Rating,
    DateTime? PublishDate,
    string? Author,
    decimal? ReadingMinutes,
    string? Brand,
    decimal? Price,
    int? StockLevel);

public sealed record ShowdownSearchResponse(
    SearchProviderKind Provider,
    string IndexAlias,
    long Total,
    double ProviderMilliseconds,
    IReadOnlyList<ShowdownFacet> Facets,
    IReadOnlyList<ShowdownHit> Hits,
    string? Error);

public sealed class ShowdownSearchService
{
    private readonly ISearcherResolver _searcherResolver;
    private readonly IContentTypeService _contentTypeService;
    private readonly ICacheManager _cacheManager;
    private readonly ShowdownProviders _providers;
    private readonly AlgoliaUsageTracker _algoliaUsage;
    private readonly ILogger<ShowdownSearchService> _logger;

    public ShowdownSearchService(
        ISearcherResolver searcherResolver,
        IContentTypeService contentTypeService,
        ICacheManager cacheManager,
        ShowdownProviders providers,
        AlgoliaUsageTracker algoliaUsage,
        ILogger<ShowdownSearchService> logger)
    {
        _searcherResolver = searcherResolver;
        _contentTypeService = contentTypeService;
        _cacheManager = cacheManager;
        _providers = providers;
        _algoliaUsage = algoliaUsage;
        _logger = logger;
    }

    public async Task<ShowdownSearchResponse> SearchAsync(ShowdownSearchRequest request, SearchProviderKind provider)
    {
        var indexAlias = ShowdownIndexes.For(provider);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        try
        {
            ISearcher searcher = _searcherResolver.GetRequiredSearcher(indexAlias);
            Filter[] filters = Filters(request).ToArray();
            Facet[] facets = request.WithoutFacets
                ? []
                : ShowdownFacets.All.Select(definition => definition.ToFacet()).ToArray();
            Sorter[] sorters = Sorters(request.Sort);
            var query = string.IsNullOrWhiteSpace(request.Q) ? null : request.Q.Trim();

            if (provider is SearchProviderKind.Algolia)
            {
                _algoliaUsage.Add(1);
            }

            var stopwatch = Stopwatch.StartNew();
            SearchResult result = await searcher.SearchAsync(
                indexAlias,
                query,
                filters,
                facets,
                sorters,
                culture: null,
                segment: null,
                accessContext: null,
                skip: (page - 1) * pageSize,
                take: pageSize);
            stopwatch.Stop();

            return new ShowdownSearchResponse(
                provider,
                indexAlias,
                result.Total,
                stopwatch.Elapsed.TotalMilliseconds,
                result.Facets.Select(ToFacet).OfType<ShowdownFacet>().ToArray(),
                result.Documents.Select(ToHit).OfType<ShowdownHit>().ToArray(),
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for provider {Provider}", provider);
            return new ShowdownSearchResponse(provider, indexAlias, 0, 0, [], [], ex.Message);
        }
    }

    public async Task<IReadOnlyList<ShowdownSearchResponse>> CompareAsync(ShowdownSearchRequest request)
    {
        var responses = new List<ShowdownSearchResponse>();
        foreach (SearchProviderKind provider in _providers.Available)
        {
            responses.Add(await SearchAsync(request, provider));
        }

        return responses;
    }

    private IEnumerable<Filter> Filters(ShowdownSearchRequest request)
    {
        var contentTypeAliases = request.Type switch
        {
            "article" => new[] { DemoContentManager.ArticleAlias },
            "product" => new[] { DemoContentManager.ProductAlias },
            _ => new[] { DemoContentManager.ArticleAlias, DemoContentManager.ProductAlias },
        };

        var contentTypeKeys = contentTypeAliases
            .Select(alias => _contentTypeService.Get(alias)?.Key.ToString())
            .OfType<string>()
            .ToArray();

        yield return new KeywordFilter(CoreFieldNames.ContentTypeId, contentTypeKeys, false);

        foreach (ShowdownFacetDefinition definition in ShowdownFacets.All)
        {
            Filter? filter = definition.ToFilter(request.SelectedFor(definition.Field));
            if (filter is not null)
            {
                yield return filter;
            }
        }
    }

    private static Sorter[] Sorters(string? sort)
        => sort switch
        {
            ShowdownSorts.Newest => [new DateTimeOffsetSorter("publishDate", Direction.Descending)],
            ShowdownSorts.PriceAscending => [new DecimalSorter("price", Direction.Ascending)],
            ShowdownSorts.PriceDescending => [new DecimalSorter("price", Direction.Descending)],
            ShowdownSorts.TopRated => [new IntegerSorter("rating", Direction.Descending)],
            _ => [new ScoreSorter(Direction.Descending)],
        };

    private ShowdownFacet? ToFacet(FacetResult facet)
    {
        ShowdownFacetDefinition? definition = ShowdownFacets.Find(facet.FieldName);
        if (definition is null)
        {
            return null;
        }

        IEnumerable<ShowdownFacetValue> values = facet.Values
            .Select(value => value switch
            {
                KeywordFacetValue keyword => (Key: keyword.Key, keyword.Count),
                IntegerExactFacetValue exact => (Key: exact.Key.ToString(), exact.Count),
                IntegerRangeFacetValue range => (Key: range.Key, range.Count),
                DecimalRangeFacetValue range => (Key: range.Key, range.Count),
                DateTimeOffsetRangeFacetValue range => (Key: range.Key, range.Count),
                _ => (Key: (string?)null, value.Count),
            })
            .Where(value => value.Key is not null)
            .Select(value => new ShowdownFacetValue(value.Key!, Label(definition, value.Key!), value.Count));

        return new ShowdownFacet(definition.Field, definition.Title, definition.Order(values).ToArray());
    }

    private string Label(ShowdownFacetDefinition definition, string key)
        => definition is KeywordFacetDefinition { LabelsFromContent: true } && Guid.TryParse(key, out Guid contentKey)
            ? _cacheManager.Content.GetById(contentKey)?.Name ?? key
            : definition.Label(key);

    private ShowdownHit? ToHit(Document document)
    {
        IPublishedContent? content = _cacheManager.Content.GetById(document.Id);
        if (content is null)
        {
            return null;
        }

        return new ShowdownHit(
            content.Key,
            content.Name,
            content.ContentType.Alias,
            content.Value<string>("summary"),
            content.Value<string>("category"),
            content.Value<string>("audience"),
            content.Value<IPublishedContent>("topic")?.Name,
            content.Value<bool>("featured"),
            content.Value<int?>("rating"),
            content.Value<DateTime?>("publishDate"),
            content.Value<string>("author"),
            content.Value<decimal?>("readingMinutes"),
            content.Value<string>("brand"),
            content.Value<decimal?>("price"),
            content.Value<int?>("stockLevel"));
    }
}
