using Examine;
using Examine.Lucene.Providers;
using Kjac.SearchProvider.Algolia.DependencyInjection;
using Kjac.SearchProvider.Algolia.Extensions;
using SearchShowdown.AzureSearch.Configuration;
using SearchShowdown.AzureSearch.DependencyInjection;
using SearchShowdown.Search;
using SearchShowdown.Seeding;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.BackOffice.DependencyInjection;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.DependencyInjection;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.DeliveryApi.DependencyInjection;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Cms.Search.Provider.Examine.DependencyInjection;
using Umbraco.Cms.Search.Provider.Examine.Services;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace SearchShowdown.DependencyInjection;

public sealed class SiteComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        ShowdownProviders providers = ShowdownProviders.FromConfiguration(builder.Config);
        builder.Services.AddSingleton(providers);
        builder.Services.AddSingleton<AlgoliaUsageTracker>();

        builder.AddSearchCore();

        if (providers.Algolia)
        {
            builder.AddAlgoliaSearchProvider();
        }

        builder
            .AddExamineSearchProvider()
            .AddDeliveryApi()
            .AddDeliveryApiSearch()
            .AddBackOfficeSearch();

        if (providers.Azure)
        {
            builder.AddAzureSearchProvider();
        }

        builder.Services.AddExamineLuceneIndex<LuceneIndex, ConfigurationEnabledDirectoryFactory>(ShowdownIndexes.Examine, _ => { });

        builder.Services.Configure<SearcherOptions>(options => options.ExpandFacetValues = true);
        builder.Services.Configure<FieldOptions>(options => options.Fields = ShowdownFields.ForExamine());
        builder.Services.Configure<AzureSearchFieldOptions>(options => options.Fields = ShowdownFields.ForAzure());

        builder.Services.Configure<IndexOptions>(options =>
        {
            options.RegisterContentIndex<IExamineIndexer, IExamineSearcher, IPublishedContentChangeStrategy>(ShowdownIndexes.Examine, UmbracoObjectTypes.Document);

            if (providers.Algolia)
            {
                options.RegisterAlgoliaContentIndex<IPublishedContentChangeStrategy>(ShowdownIndexes.Algolia, UmbracoObjectTypes.Document);
            }

            if (providers.Azure)
            {
                options.RegisterAzureSearchContentIndex<IPublishedContentChangeStrategy>(ShowdownIndexes.Azure, UmbracoObjectTypes.Document);
            }
        });

        builder.Services.AddTransient<IContentIndexer, RangeBucketContentIndexer>();

        if (providers.Algolia)
        {
            builder.AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, AlgoliaIndexSetupNotificationHandler>();
        }

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, RebuildEmptyIndexesNotificationHandler>();

        builder.Services.AddTransient<DemoContentManager>();
        builder.Services.AddTransient<ShowdownSearchService>();
        builder.Services.AddTransient<ShowdownIndexService>();
    }
}
