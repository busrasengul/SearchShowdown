# Search Showdown — Examine vs Algolia vs Azure AI Search on Umbraco Search

Demo site for the talk **"The Great Umbraco Search Shake-Up"** (Umbraco UK Festival 2026).

One Umbraco 17 site, one generated content set, three search providers running side by side on the new
[Umbraco Search](https://github.com/umbraco/Umbraco.Cms.Search) provider model:

- **Examine** — `Umbraco.Cms.Search.Provider.Examine`
- **Algolia** — [`Kjac.SearchProvider.Algolia`](https://github.com/kjac/Kjac.SearchProvider.Algolia)
- **Azure AI Search** — a custom provider in [`SearchShowdown.AzureSearch`](SearchShowdown.AzureSearch)

## Stack

- Umbraco CMS 17.7.1 (.NET 10), SQLite
- Umbraco Search 17.2.0 (Core, Examine provider, Delivery API, BackOffice)
- [Clean starter kit](https://github.com/prjseal/Clean) 7.0.8
- [Bogus](https://github.com/bchavez/Bogus) for reproducible demo content

## Getting started

```bash
dotnet run --project SearchShowdown
```

The first run performs an unattended install using the credentials in `appsettings.Development.json`.
Change them before using this anywhere other than your own machine.

Generate the demo content (5,000 articles and products):

```bash
curl -X POST "http://localhost:22518/api/seed/generate?count=5000"
```

Then open `/showdown`.

## Configuring the cloud providers

Examine works out of the box. Algolia and Azure AI Search switch on automatically when credentials are present.
Use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) so nothing sensitive is committed:

```bash
dotnet user-secrets set "AlgoliaSearchProvider:Client:AppId" "<app id>" --project SearchShowdown
dotnet user-secrets set "AlgoliaSearchProvider:Client:ApiKey" "<write api key>" --project SearchShowdown
dotnet user-secrets set "AzureSearchProvider:Endpoint" "https://<service>.search.windows.net" --project SearchShowdown
dotnet user-secrets set "AzureSearchProvider:ApiKey" "<admin api key>" --project SearchShowdown
```

On startup the site creates and configures the `Showdown_Algolia` and `showdown-azure` indexes, and rebuilds them if they are empty.

## Endpoints (development only)

| Method | Path | Purpose |
|---|---|---|
| GET | `/showdown` | Search page with provider switcher and comparison |
| GET | `/api/showdown/search?provider=Azure&q=cloud` | JSON search result with provider time |
| GET | `/api/showdown/compare?q=cloud` | Same query against every configured provider |
| GET | `/api/showdown/indexes` | Document count and health per provider |
| POST | `/api/showdown/rebuild?provider=Algolia&clearFieldCache=true` | Rebuild a provider index |
| POST | `/api/seed/generate?count=5000` | Generate demo content |
| POST | `/api/seed/reset` | Recreate the demo content types (deletes demo content) |
| GET | `/api/seed/status` | Published demo content counts |

Add `facets=none` to a search URL to run it without requesting any facets.

## Benchmark

With the site running, measure every configured provider (Node.js 22 or later):

```bash
node benchmark/run.mjs --warmup=5 --iterations=50
```

Each run writes `results.json`, `raw.csv` and `summary.md` to `private/benchmark/<timestamp>`. Useful options:

- `--scenarios=keyword,keyword-no-facets` runs specific searches only. The available searches are `keyword`, `keyword-facet`, `price-range`, `sort-price`, `typo`, `browse-all`, `keyword-no-facets` and `browse-no-facets`.
- `--providers=Examine,Azure` runs specific providers only.

Create slide-sized charts from a run:

```bash
node benchmark/charts.mjs --main=private/benchmark/<run> --facets=private/benchmark/<facet-run>
```

Algolia's free plan allows 10,000 search requests a month. A default run uses 330 of them.
