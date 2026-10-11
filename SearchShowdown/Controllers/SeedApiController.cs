using Microsoft.AspNetCore.Mvc;
using SearchShowdown.Seeding;

namespace SearchShowdown.Controllers;

[ApiController]
[Route("api/seed")]
public sealed class SeedApiController : ControllerBase
{
    private readonly DemoContentManager _manager;
    private readonly IWebHostEnvironment _environment;

    public SeedApiController(DemoContentManager manager, IWebHostEnvironment environment)
    {
        _manager = manager;
        _environment = environment;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromQuery] int count = 5000)
    {
        if (_environment.IsDevelopment() is false)
        {
            return NotFound();
        }

        DateTime started = DateTime.UtcNow;
        var created = await _manager.GenerateAsync(count);
        return Ok(new { created, seconds = (DateTime.UtcNow - started).TotalSeconds });
    }

    [HttpPost("clear")]
    public IActionResult Clear()
        => _environment.IsDevelopment() ? Ok(new { deleted = _manager.Clear() }) : NotFound();

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (_environment.IsDevelopment() is false)
        {
            return NotFound();
        }

        await _manager.ResetModelAsync();
        await _manager.EnsureModelAsync();
        return Ok(new { reset = true });
    }

    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new { counts = _manager.PublishedCounts() });
}
