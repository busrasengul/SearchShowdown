using Microsoft.AspNetCore.Mvc;
using SearchShowdown.Search;

namespace SearchShowdown.Controllers;

public sealed record ShowdownPageViewModel(
    ShowdownSearchRequest Request,
    ShowdownSearchResponse Result,
    IReadOnlyList<ShowdownSearchResponse> Comparison,
    IReadOnlyList<SearchProviderKind> AvailableProviders,
    long AlgoliaSearches);

public sealed class ShowdownController : Controller
{
    private readonly ShowdownSearchService _searchService;
    private readonly ShowdownProviders _providers;
    private readonly AlgoliaUsageTracker _algoliaUsage;

    public ShowdownController(ShowdownSearchService searchService, ShowdownProviders providers, AlgoliaUsageTracker algoliaUsage)
    {
        _searchService = searchService;
        _providers = providers;
        _algoliaUsage = algoliaUsage;
    }

    [HttpGet("/showdown")]
    public async Task<IActionResult> Index()
    {
        var request = ShowdownSearchRequest.FromQuery(Request.Query);
        if (_providers.IsAvailable(request.Provider) is false)
        {
            request.Provider = SearchProviderKind.Examine;
        }

        ShowdownSearchResponse result = await _searchService.SearchAsync(request, request.Provider);
        IReadOnlyList<ShowdownSearchResponse> comparison = request.Compare
            ? await _searchService.CompareAsync(request)
            : [];

        return View(
            "~/Views/Showdown/Index.cshtml",
            new ShowdownPageViewModel(request, result, comparison, _providers.Available, _algoliaUsage.Searches));
    }
}

[ApiController]
[Route("api/showdown")]
public sealed class ShowdownApiController : ControllerBase
{
    private readonly ShowdownSearchService _searchService;
    private readonly ShowdownIndexService _indexService;
    private readonly ShowdownProviders _providers;
    private readonly AlgoliaUsageTracker _algoliaUsage;
    private readonly IWebHostEnvironment _environment;

    public ShowdownApiController(
        ShowdownSearchService searchService,
        ShowdownIndexService indexService,
        ShowdownProviders providers,
        AlgoliaUsageTracker algoliaUsage,
        IWebHostEnvironment environment)
    {
        _searchService = searchService;
        _indexService = indexService;
        _providers = providers;
        _algoliaUsage = algoliaUsage;
        _environment = environment;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search()
    {
        var request = ShowdownSearchRequest.FromQuery(Request.Query);
        return _providers.IsAvailable(request.Provider)
            ? Ok(await _searchService.SearchAsync(request, request.Provider))
            : BadRequest($"Provider {request.Provider} is not configured.");
    }

    [HttpGet("compare")]
    public async Task<IActionResult> Compare()
        => Ok(await _searchService.CompareAsync(ShowdownSearchRequest.FromQuery(Request.Query)));

    [HttpGet("indexes")]
    public async Task<IActionResult> Indexes()
        => Ok(new { indexes = await _indexService.StatusAsync(), algoliaSearchesThisSession = _algoliaUsage.Searches });

    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild([FromQuery] SearchProviderKind provider, [FromQuery] bool clearFieldCache = false)
    {
        if (_environment.IsDevelopment() is false)
        {
            return NotFound();
        }

        if (_providers.IsAvailable(provider) is false)
        {
            return BadRequest($"Provider {provider} is not configured.");
        }

        return Ok(new { provider, started = await _indexService.RebuildAsync(provider, clearFieldCache) });
    }
}
