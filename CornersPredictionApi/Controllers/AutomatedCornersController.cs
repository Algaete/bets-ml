using AutomatedCornersBot.Api;
using CornersPrediction.Application.AutomatedCorners;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/automated-corners")]
public sealed class AutomatedCornersController : ControllerBase
{
    private const string PerformanceScorecardsCacheKey = "automated-bot-performance-scorecards-v1";
    private readonly IGetAutomatedCornerSelectionsUseCase _getSelectionsUseCase;
    private readonly IUpdateAutomatedCornerSelectionStatusUseCase _updateSelectionStatusUseCase;
    private readonly IResolveAutomatedCornerSelectionUseCase _resolveSelectionUseCase;
    private readonly ILinkAutomatedCornerSelectionMatchUseCase _linkSelectionMatchUseCase;
    private readonly IDeleteAutomatedCornerSelectionUseCase _deleteSelectionUseCase;
    private readonly IAutomatedBotPickSettlementUseCase _settlementUseCase;
    private readonly IAutomatedBotPerformanceService _performanceService;
    private readonly IMemoryCache _cache;
    private readonly AutomatedCornersSelectionService _selectionService;
    private readonly SqlAutomationRepository _automationRepository;
    private readonly ILogger<AutomatedCornersController> _logger;

    public AutomatedCornersController(
        IGetAutomatedCornerSelectionsUseCase getSelectionsUseCase,
        IUpdateAutomatedCornerSelectionStatusUseCase updateSelectionStatusUseCase,
        IResolveAutomatedCornerSelectionUseCase resolveSelectionUseCase,
        ILinkAutomatedCornerSelectionMatchUseCase linkSelectionMatchUseCase,
        IDeleteAutomatedCornerSelectionUseCase deleteSelectionUseCase,
        IAutomatedBotPickSettlementUseCase settlementUseCase,
        IAutomatedBotPerformanceService performanceService,
        IMemoryCache cache,
        AutomatedCornersSelectionService selectionService,
        SqlAutomationRepository automationRepository,
        ILogger<AutomatedCornersController> logger)
    {
        _getSelectionsUseCase = getSelectionsUseCase;
        _updateSelectionStatusUseCase = updateSelectionStatusUseCase;
        _resolveSelectionUseCase = resolveSelectionUseCase;
        _linkSelectionMatchUseCase = linkSelectionMatchUseCase;
        _deleteSelectionUseCase = deleteSelectionUseCase;
        _settlementUseCase = settlementUseCase;
        _performanceService = performanceService;
        _cache = cache;
        _selectionService = selectionService;
        _automationRepository = automationRepository;
        _logger = logger;
    }

    [HttpGet("performance/scorecards")]
    [ProducesResponseType(typeof(IReadOnlyList<AutomatedBotPerformanceScorecard>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerformanceScorecards(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scorecards = await _cache.GetOrCreateAsync(
                PerformanceScorecardsCacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return await _performanceService.GetScorecardsAsync(cancellationToken);
                });
            return Ok(scorecards ?? []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to compute automated bot performance scorecards");
            return Problem(
                title: "Could not compute bot performance scorecards",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("run")]
    [ProducesResponseType(typeof(AutomatedRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Run(
        [FromBody] RunAutomatedCornersRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _automationRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _selectionService.RunAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to run automated corners bots");
            return Problem(
                title: "Could not run automated corners bots",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("availability")]
    [ProducesResponseType(typeof(AutomatedOddsAvailabilityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _automationRepository.EnsureSchemaAsync(cancellationToken);
        return Ok(await _selectionService.GetAvailabilityAsync(batchSize, cancellationToken));
    }

    [HttpPost("settle")]
    [ProducesResponseType(typeof(AutomatedBotPickSettlementResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Settle(
        [FromBody] AutomatedBotPickSettlementRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _automationRepository.EnsureSchemaAsync(cancellationToken);
            var result = await _settlementUseCase.SettleAsync(
                request ?? new AutomatedBotPickSettlementRequest(),
                cancellationToken);
            _cache.Remove(PerformanceScorecardsCacheKey);
            return Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to settle automated bot picks from local MatchHistory");
            return Problem(
                title: "Could not settle automated bot picks",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("selections")]
    [ProducesResponseType(typeof(IReadOnlyList<AutomatedCornerSelectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSelections(
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? status,
        [FromQuery] string? league,
        [FromQuery] string? source,
        [FromQuery] string? marketType,
        [FromQuery] bool onlyPending = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filters = new AutomatedCornerSelectionsFilterRequest(
                dateFrom,
                dateTo,
                status,
                league,
                source,
                marketType,
                onlyPending);
            var selections = await _getSelectionsUseCase.GetAsync(filters, cancellationToken);
            return Ok(selections);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load automated corner selections");
            return Problem(
                title: "Could not load automated corner selections",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("selections/{id:long}/status")]
    [ProducesResponseType(typeof(AutomatedCornerSelectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSelectionStatus(
        [FromRoute] long id,
        [FromBody] UpdateAutomatedCornerSelectionStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updatedSelection = await _updateSelectionStatusUseCase.UpdateAsync(id, request, cancellationToken);
            _cache.Remove(PerformanceScorecardsCacheKey);
            return Ok(updatedSelection);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update automated corner selection {SelectionId}", id);
            return Problem(
                title: "Could not update automated corner selection status",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("selections/{id:long}/resolve")]
    [ProducesResponseType(typeof(AutomatedCornerSelectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveSelection(
        [FromRoute] long id,
        [FromBody] ResolveAutomatedCornerSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _automationRepository.EnsureSchemaAsync(cancellationToken);
            var updatedSelection = await _resolveSelectionUseCase.ResolveAsync(id, request, cancellationToken);
            _cache.Remove(PerformanceScorecardsCacheKey);
            return Ok(updatedSelection);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to resolve automated corner selection {SelectionId}", id);
            return Problem(
                title: "Could not resolve automated corner selection",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("selections/{id:long}/match-history-link")]
    [ProducesResponseType(typeof(AutomatedCornerSelectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkSelectionMatch(
        [FromRoute] long id,
        [FromBody] LinkAutomatedCornerSelectionMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updatedSelection = await _linkSelectionMatchUseCase.LinkAsync(id, request, cancellationToken);
            return Ok(updatedSelection);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to link automated selection {SelectionId} to MatchHistory", id);
            return Problem(
                title: "Could not link automated selection to MatchHistory",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete("selections/{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSelection(
        [FromRoute] long id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleted = await _deleteSelectionUseCase.DeleteAsync(id, cancellationToken);
            if (deleted)
                _cache.Remove(PerformanceScorecardsCacheKey);
            return deleted ? NoContent() : NotFound(new { error = $"Automated corner selection {id} was not found." });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete automated corner selection {SelectionId}", id);
            return Problem(
                title: "Could not delete automated corner selection",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
