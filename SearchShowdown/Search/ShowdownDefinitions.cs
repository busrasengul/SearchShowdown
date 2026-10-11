using System.Globalization;
using SearchShowdown.AzureSearch.Configuration;
using Umbraco.Cms.Search.Core.Models.Searching.Faceting;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Provider.Examine.Configuration;

namespace SearchShowdown.Search;

public enum SearchProviderKind
{
    Examine,
    Algolia,
    Azure,
}

public enum ShowdownFieldType
{
    Keywords,
    Integers,
    Decimals,
    DateTimeOffsets,
}

public sealed record ShowdownField(string Alias, ShowdownFieldType Type, bool Facetable, bool Sortable);

public static class ShowdownIndexes
{
    public const string Examine = "Showdown_Examine";
    public const string Algolia = "Showdown_Algolia";
    public const string Azure = "Showdown_Azure";

    public static string For(SearchProviderKind provider)
        => provider switch
        {
            SearchProviderKind.Algolia => Algolia,
            SearchProviderKind.Azure => Azure,
            _ => Examine,
        };
}

public abstract class ShowdownFacetDefinition
{
    protected ShowdownFacetDefinition(string field, string title, ShowdownFieldType type)
    {
        Field = field;
        Title = title;
        Type = type;
    }

    public string Field { get; }

    public string Title { get; }

    public ShowdownFieldType Type { get; }

    public abstract Facet ToFacet();

    public abstract Filter? ToFilter(string[] selectedKeys);

    public virtual string Label(string key) => key;

    public virtual IEnumerable<ShowdownFacetValue> Order(IEnumerable<ShowdownFacetValue> values)
        => values.OrderByDescending(value => value.Count).ThenBy(value => value.Label);
}

public sealed class KeywordFacetDefinition : ShowdownFacetDefinition
{
    public KeywordFacetDefinition(string field, string title, bool labelsFromContent = false)
        : base(field, title, ShowdownFieldType.Keywords)
        => LabelsFromContent = labelsFromContent;

    public bool LabelsFromContent { get; }

    public override Facet ToFacet() => new KeywordFacet(Field);

    public override Filter? ToFilter(string[] selectedKeys)
        => selectedKeys.Length > 0 ? new KeywordFilter(Field, selectedKeys, false) : null;
}

public sealed class IntegerExactFacetDefinition : ShowdownFacetDefinition
{
    private readonly Func<int, string> _label;

    public IntegerExactFacetDefinition(string field, string title, Func<int, string> label)
        : base(field, title, ShowdownFieldType.Integers)
        => _label = label;

    public override Facet ToFacet() => new IntegerExactFacet(Field);

    public override Filter? ToFilter(string[] selectedKeys)
    {
        var values = selectedKeys
            .Select(key => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null)
            .OfType<int>()
            .ToArray();
        return values.Length > 0 ? new IntegerExactFilter(Field, values, false) : null;
    }

    public override string Label(string key)
        => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? _label(value) : key;

    public override IEnumerable<ShowdownFacetValue> Order(IEnumerable<ShowdownFacetValue> values)
        => values.OrderByDescending(value => int.TryParse(value.Key, out var number) ? number : int.MinValue);
}

public sealed record ShowdownRange<T>(string Key, string Label, T? Min, T? Max)
    where T : struct, IComparable<T>
{
    public bool Contains(T value)
        => (Min is null || value.CompareTo(Min.Value) >= 0) && (Max is null || value.CompareTo(Max.Value) < 0);
}

public interface IRangeFacetDefinition
{
    string Field { get; }

    string? BucketFor(object? propertyValue);
}

public abstract class RangeFacetDefinition<T> : ShowdownFacetDefinition, IRangeFacetDefinition
    where T : struct, IComparable<T>
{
    protected RangeFacetDefinition(string field, string title, ShowdownFieldType type, ShowdownRange<T>[] ranges)
        : base(field, title, type)
        => Ranges = ranges;

    public ShowdownRange<T>[] Ranges { get; }

    public override string Label(string key)
        => Ranges.FirstOrDefault(range => range.Key == key)?.Label ?? key;

    public override IEnumerable<ShowdownFacetValue> Order(IEnumerable<ShowdownFacetValue> values)
        => values.OrderBy(value => Array.FindIndex(Ranges, range => range.Key == value.Key));

    public string? BucketFor(object? propertyValue)
        => Convert(propertyValue) is { } value
            ? Ranges.FirstOrDefault(range => range.Contains(value))?.Key
            : null;

    protected ShowdownRange<T>[] Selected(string[] selectedKeys)
        => Ranges.Where(range => selectedKeys.Contains(range.Key, StringComparer.OrdinalIgnoreCase)).ToArray();

    protected abstract T? Convert(object? propertyValue);
}

public sealed class IntegerRangeFacetDefinition : RangeFacetDefinition<int>
{
    public IntegerRangeFacetDefinition(string field, string title, ShowdownRange<int>[] ranges)
        : base(field, title, ShowdownFieldType.Integers, ranges)
    {
    }

    public override Facet ToFacet()
        => new IntegerRangeFacet(Field, Ranges.Select(range => new IntegerRangeFacetRange(range.Key, range.Min, range.Max)).ToArray());

    public override Filter? ToFilter(string[] selectedKeys)
    {
        ShowdownRange<int>[] selected = Selected(selectedKeys);
        return selected.Length > 0
            ? new IntegerRangeFilter(Field, selected.Select(range => new IntegerRangeFilterRange(range.Min, range.Max)).ToArray(), false)
            : null;
    }

    protected override int? Convert(object? propertyValue)
        => propertyValue switch
        {
            int value => value,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
}

public sealed class DecimalRangeFacetDefinition : RangeFacetDefinition<decimal>
{
    public DecimalRangeFacetDefinition(string field, string title, ShowdownRange<decimal>[] ranges)
        : base(field, title, ShowdownFieldType.Decimals, ranges)
    {
    }

    public override Facet ToFacet()
        => new DecimalRangeFacet(Field, Ranges.Select(range => new DecimalRangeFacetRange(range.Key, range.Min, range.Max)).ToArray());

    public override Filter? ToFilter(string[] selectedKeys)
    {
        ShowdownRange<decimal>[] selected = Selected(selectedKeys);
        return selected.Length > 0
            ? new DecimalRangeFilter(Field, selected.Select(range => new DecimalRangeFilterRange(range.Min, range.Max)).ToArray(), false)
            : null;
    }

    protected override decimal? Convert(object? propertyValue)
        => propertyValue switch
        {
            decimal value => value,
            int value => value,
            double value => (decimal)value,
            string text when decimal.TryParse(text.Split(',')[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
}

public sealed class DateRangeFacetDefinition : RangeFacetDefinition<DateTimeOffset>
{
    public DateRangeFacetDefinition(string field, string title, ShowdownRange<DateTimeOffset>[] ranges)
        : base(field, title, ShowdownFieldType.DateTimeOffsets, ranges)
    {
    }

    public override Facet ToFacet()
        => new DateTimeOffsetRangeFacet(Field, Ranges.Select(range => new DateTimeOffsetRangeFacetRange(range.Key, range.Min, range.Max)).ToArray());

    public override Filter? ToFilter(string[] selectedKeys)
    {
        ShowdownRange<DateTimeOffset>[] selected = Selected(selectedKeys);
        return selected.Length > 0
            ? new DateTimeOffsetRangeFilter(Field, selected.Select(range => new DateTimeOffsetRangeFilterRange(range.Min, range.Max)).ToArray(), false)
            : null;
    }

    protected override DateTimeOffset? Convert(object? propertyValue)
        => propertyValue switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            _ => null,
        };
}

public static class ShowdownFacets
{
    private const decimal OpenEndedDecimalMaximum = 1_000_000m;

    private static DateTimeOffset Year(int year) => new(year, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly ShowdownFacetDefinition[] All =
    [
        new KeywordFacetDefinition("category", "Category"),
        new KeywordFacetDefinition("topic", "Topic", labelsFromContent: true),
        new KeywordFacetDefinition("audience", "Audience"),
        new KeywordFacetDefinition("regions", "Region"),
        new KeywordFacetDefinition("tags", "Tags"),
        new IntegerExactFacetDefinition("featured", "Featured", value => value == 1 ? "Featured" : "Standard"),
        new IntegerExactFacetDefinition("rating", "Rating", value => new string('★', Math.Clamp(value, 0, 5))),
        new DateRangeFacetDefinition("publishDate", "Published",
        [
            new("2025", "2025", Year(2025), Year(2026)),
            new("2024", "2024", Year(2024), Year(2025)),
            new("2022-2023", "2022 – 2023", Year(2022), Year(2024)),
            new("2019-2021", "2019 – 2021", Year(2019), Year(2022)),
            new("before-2019", "Before 2019", null, Year(2019)),
        ]),
        new KeywordFacetDefinition("brand", "Brand"),
        new DecimalRangeFacetDefinition("price", "Price",
        [
            new("under-50", "Under £50", null, 50m),
            new("50-200", "£50 – £200", 50m, 200m),
            new("200-500", "£200 – £500", 200m, 500m),
            new("500-1000", "£500 – £1,000", 500m, 1000m),
            new("1000-plus", "£1,000+", 1000m, OpenEndedDecimalMaximum),
        ]),
        new IntegerRangeFacetDefinition("stockLevel", "Availability",
        [
            new("out-of-stock", "Out of stock", null, 1),
            new("low", "Low stock (1 – 20)", 1, 21),
            new("in-stock", "In stock (21 – 200)", 21, 201),
            new("plenty", "Plenty (200+)", 201, null),
        ]),
        new DecimalRangeFacetDefinition("readingMinutes", "Reading time",
        [
            new("under-5", "Under 5 min", null, 5m),
            new("5-10", "5 – 10 min", 5m, 10m),
            new("10-20", "10 – 20 min", 10m, 20m),
            new("20-plus", "20+ min", 20m, OpenEndedDecimalMaximum),
        ]),
    ];

    public static ShowdownFacetDefinition? Find(string field)
        => All.FirstOrDefault(definition => string.Equals(definition.Field, field, StringComparison.OrdinalIgnoreCase));
}

public static class ShowdownSorts
{
    public const string Newest = "newest";
    public const string PriceAscending = "price-asc";
    public const string PriceDescending = "price-desc";
    public const string TopRated = "rating";

    public static readonly (string Key, string Label)[] Options =
    [
        ("", "Most relevant"),
        (Newest, "Newest"),
        (PriceAscending, "Price: low to high"),
        (PriceDescending, "Price: high to low"),
        (TopRated, "Top rated"),
    ];
}

public static class ShowdownFields
{
    private static readonly string[] SortableFields = ["rating", "publishDate", "price"];

    public static ShowdownField[] All
        => ShowdownFacets.All
            .Select(definition => new ShowdownField(definition.Field, definition.Type, true, SortableFields.Contains(definition.Field)))
            .ToArray();

    public static FieldOptions.Field[] ForExamine()
        => All.Select(field => new FieldOptions.Field
        {
            PropertyName = field.Alias,
            FieldValues = field.Type switch
            {
                ShowdownFieldType.Keywords => FieldValues.Keywords,
                ShowdownFieldType.Integers => FieldValues.Integers,
                ShowdownFieldType.Decimals => FieldValues.Decimals,
                _ => FieldValues.DateTimeOffsets,
            },
            Facetable = field.Facetable,
            Sortable = field.Sortable,
        }).ToArray();

    public static AzureSearchFieldOptions.Field[] ForAzure()
        => All.Select(field => new AzureSearchFieldOptions.Field
        {
            PropertyName = field.Alias,
            FieldValues = field.Type switch
            {
                ShowdownFieldType.Keywords => AzureFieldValues.Keywords,
                ShowdownFieldType.Integers => AzureFieldValues.Integers,
                ShowdownFieldType.Decimals => AzureFieldValues.Decimals,
                _ => AzureFieldValues.DateTimeOffsets,
            },
            Facetable = field.Facetable,
            Sortable = field.Sortable,
        }).ToArray();

    public static string AlgoliaAttribute(ShowdownField field)
        => $"fields.{field.Alias}{AlgoliaPostfix(field.Type)}";

    public static string AlgoliaPostfix(ShowdownFieldType type)
        => type switch
        {
            ShowdownFieldType.Keywords => "_keywords",
            ShowdownFieldType.Integers => "_integers",
            ShowdownFieldType.Decimals => "_decimals",
            _ => "_datetimeoffsets",
        };

    public static IEnumerable<string> AlgoliaAttributesForFaceting()
    {
        foreach (ShowdownFacetDefinition definition in ShowdownFacets.All)
        {
            var attribute = AlgoliaAttribute(new ShowdownField(definition.Field, definition.Type, true, false));
            switch (definition)
            {
                case KeywordFacetDefinition:
                    yield return $"searchable({attribute})";
                    break;
                case IRangeFacetDefinition:
                    yield return $"fields.__range_{definition.Field}_keywords";
                    yield return $"filterOnly({attribute})";
                    break;
                default:
                    yield return attribute;
                    break;
            }
        }
    }
}

public sealed class ShowdownProviders
{
    public bool Algolia { get; init; }

    public bool Azure { get; init; }

    public IReadOnlyList<SearchProviderKind> Available
        => new[] { SearchProviderKind.Examine }
            .Concat(Algolia ? [SearchProviderKind.Algolia] : [])
            .Concat(Azure ? [SearchProviderKind.Azure] : [])
            .ToArray();

    public bool IsAvailable(SearchProviderKind provider) => Available.Contains(provider);

    public static ShowdownProviders FromConfiguration(IConfiguration configuration)
        => new()
        {
            Algolia = string.IsNullOrWhiteSpace(configuration["AlgoliaSearchProvider:Client:AppId"]) is false
                      && string.IsNullOrWhiteSpace(configuration["AlgoliaSearchProvider:Client:ApiKey"]) is false,
            Azure = string.IsNullOrWhiteSpace(configuration["AzureSearchProvider:Endpoint"]) is false
                    && string.IsNullOrWhiteSpace(configuration["AzureSearchProvider:ApiKey"]) is false,
        };
}

public sealed class AlgoliaUsageTracker
{
    private long _searches;

    public long Searches => Interlocked.Read(ref _searches);

    public void Add(int searches) => Interlocked.Add(ref _searches, searches);
}
