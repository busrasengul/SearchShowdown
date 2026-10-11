using Algolia.Search.Clients;
using Algolia.Search.Models.Search;
using Kjac.SearchProvider.Algolia.Configuration;
using Kjac.SearchProvider.Algolia.Services;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace SearchShowdown.Search;

public sealed class AlgoliaIndexSetupNotificationHandler : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private static readonly (string Attribute, bool Ascending)[] SortReplicas =
    [
        ("publishDate_datetimeoffsets", false),
        ("price_decimals", true),
        ("price_decimals", false),
        ("rating_integers", false),
    ];

    private readonly IAlgoliaIndexManager _indexManager;
    private readonly ClientOptions _clientOptions;
    private readonly ILogger<AlgoliaIndexSetupNotificationHandler> _logger;

    public AlgoliaIndexSetupNotificationHandler(
        IAlgoliaIndexManager indexManager,
        IOptions<ClientOptions> clientOptions,
        ILogger<AlgoliaIndexSetupNotificationHandler> logger)
    {
        _indexManager = indexManager;
        _clientOptions = clientOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _indexManager.EnsureAsync(ShowdownIndexes.Algolia);

            var client = new SearchClient(new SearchConfig(_clientOptions.AppId, _clientOptions.ApiKey));
            SettingsResponse current = await client.GetSettingsAsync(ShowdownIndexes.Algolia, cancellationToken: cancellationToken);

            List<string> managedAttributes = ShowdownFields.AlgoliaAttributesForFaceting().ToList();
            var managedNames = managedAttributes.Select(AttributeName).ToHashSet();
            List<string> facets = (current.AttributesForFaceting ?? [])
                .Where(attribute => managedNames.Contains(AttributeName(attribute)) is false)
                .Concat(managedAttributes)
                .ToList();

            List<string> replicas = SortReplicas.Select(replica => $"virtual({ReplicaName(replica)})").ToList();

            var facetsChanged = facets.Order().SequenceEqual((current.AttributesForFaceting ?? []).Order()) is false;
            var replicasChanged = replicas.Order().SequenceEqual((current.Replicas ?? []).Order()) is false;
            if (facetsChanged || replicasChanged)
            {
                UpdatedAtResponse updated = await client.SetSettingsAsync(
                    ShowdownIndexes.Algolia,
                    new IndexSettings { AttributesForFaceting = facets, Replicas = replicas },
                    cancellationToken: cancellationToken);
                await client.WaitForTaskAsync(ShowdownIndexes.Algolia, updated.TaskID);
            }

            foreach ((string attribute, bool ascending) replica in SortReplicas)
            {
                await client.SetSettingsAsync(
                    ReplicaName(replica),
                    new IndexSettings { CustomRanking = [$"{(replica.ascending ? "asc" : "desc")}(fields.{replica.attribute})"] },
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation(
                "Algolia index {IndexName} configured with {FacetCount} facet attributes and {ReplicaCount} sort replicas",
                ShowdownIndexes.Algolia,
                facets.Count,
                replicas.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not configure the Algolia index {IndexName}", ShowdownIndexes.Algolia);
        }
    }

    private static string AttributeName(string attribute)
    {
        var start = attribute.IndexOf('(');
        return start >= 0 ? attribute[(start + 1)..attribute.LastIndexOf(')')] : attribute;
    }

    private static string ReplicaName((string Attribute, bool Ascending) replica)
        => $"{ShowdownIndexes.Algolia}_{replica.Attribute}_{(replica.Ascending ? "asc" : "desc")}";
}
