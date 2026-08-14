using CornersPredictionApi.ApiFootball;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/api-football")]
public sealed class ApiFootballController : ControllerBase
{
    private readonly ApiFootballSyncService _service;
    private readonly ApiFootballClient _client;
    private readonly ApiFootballHistoricalBatchCoordinator _historicalBatchCoordinator;
    private readonly ApiFootballUpcomingMatchesSyncService _upcomingMatchesService;
    private readonly ApiFootballBotPickReconciliationService _botPickReconciliationService;
    private readonly ILogger<ApiFootballController> _logger;

    public ApiFootballController(
        ApiFootballSyncService service,
        ApiFootballClient client,
        ApiFootballHistoricalBatchCoordinator historicalBatchCoordinator,
        ApiFootballUpcomingMatchesSyncService upcomingMatchesService,
        ApiFootballBotPickReconciliationService botPickReconciliationService,
        ILogger<ApiFootballController> logger)
    {
        _service = service;
        _client = client;
        _historicalBatchCoordinator = historicalBatchCoordinator;
        _upcomingMatchesService = upcomingMatchesService;
        _botPickReconciliationService = botPickReconciliationService;
        _logger = logger;
    }

    [HttpPost("reconcile-bot-picks")]
    [ProducesResponseType(typeof(ApiFootballBotPickReconciliationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReconcileBotPicks(
        [FromBody] ApiFootballBotPickReconciliationRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _botPickReconciliationService.ReconcileAsync(
                request ?? new ApiFootballBotPickReconciliationRequest(),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reconcile all Bot Picks with API-Football and MatchHistory");
            return Problem(
                title: "Could not reconcile Bot Picks",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("sync-upcoming")]
    [ProducesResponseType(typeof(ApiFootballUpcomingSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SyncUpcoming(
        [FromBody] ApiFootballUpcomingSyncRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _upcomingMatchesService.SyncAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "API-Football upcoming fixture synchronization failed");
            return Problem(exception.Message);
        }
    }

    [HttpGet("fixtures")]
    public async Task<IActionResult> GetFinishedFixtures(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _client.GetFixturesForDateAsync(date, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load API-Football fixtures for {Date}", date);
            return Problem(exception.Message);
        }
    }

    [HttpGet("fixtures/{fixtureId:long}/statistics")]
    public async Task<IActionResult> GetFixtureStatistics(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _client.GetFixtureStatisticsAsync(fixtureId, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load API-Football statistics for fixture {FixtureId}", fixtureId);
            return Problem(exception.Message);
        }
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

    [HttpGet("historical-batch")]
    [ProducesResponseType(typeof(ApiFootballHistoricalBatchState), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistoricalBatch(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _historicalBatchCoordinator.GetStateAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not read the API-Football historical batch state");
            return Problem(exception.Message);
        }
    }

    [HttpPost("historical-batch")]
    [ProducesResponseType(typeof(ApiFootballHistoricalBatchState), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartHistoricalBatch(
        [FromBody] ApiFootballHistoricalBatchRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _historicalBatchCoordinator.StartAsync(
                request ?? new ApiFootballHistoricalBatchRequest(),
                cancellationToken);
            return Accepted(state);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start the API-Football historical batch");
            return Problem(exception.Message);
        }
    }
}
