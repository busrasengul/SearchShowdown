namespace SearchShowdown.AzureSearch.Configuration;

public sealed class AzureSearchOptions
{
    public const string SectionName = "AzureSearchProvider";

    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string IndexPrefix { get; set; } = string.Empty;

    public bool FuzzySearch { get; set; } = true;

    public int MaxFacetValues { get; set; } = 100;

    public int BatchSize { get; set; } = 250;

    public int FlushDelayMilliseconds { get; set; } = 1000;

    public int MaxIndexingAttempts { get; set; } = 8;

    public bool IsConfigured
        => string.IsNullOrWhiteSpace(Endpoint) is false && string.IsNullOrWhiteSpace(ApiKey) is false;
}
