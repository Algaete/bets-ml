using CornersPredictionApi.ApiFootball;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/api-football")]
public sealed class ApiFootballController : ControllerBase
{
    private readonly ApiFootballSyncService _service;
    private readonly ILogger<ApiFootballController> _logger;

    public ApiFootballController(
        ApiFootballSyncService service,
        ILogger<ApiFootballController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiFootballStatusResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetStatusAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not read API-Football status");
            return Problem(exception.Message);
        }
    }

    [HttpGet("database-audit")]
    [ProducesResponseType(typeof(ApiFootballDatabaseAudit), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDatabaseAudit(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.GetDatabaseAuditAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not audit the API-Football database integration");
            return Problem(exception.Message);
        }
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiFootballSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync(
        [FromBody] ApiFootballSyncRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SyncAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "API-Football synchronization failed for league {LeagueId} season {Season}",
                request.LeagueId,
                request.Season);
            return Problem(exception.Message);
        }
    }

    [HttpPost("discover")]
    [ProducesResponseType(typeof(ApiFootballDiscoveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Discover(
        [FromBody] ApiFootballDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.DiscoverAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "API-Football discovery failed");
            return Problem(exception.Message);
        }
    }


    [HttpPost("bulk-sync")]
    [ProducesResponseType(typeof(ApiFootballBulkSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkSync(
        [FromBody] ApiFootballBulkSyncRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.BulkSyncAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "API-Football bulk synchronization failed");
            return Problem(exception.Message);
        }
    }
}
