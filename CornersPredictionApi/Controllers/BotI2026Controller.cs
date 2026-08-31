using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation.BotI;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

/// <summary>
/// API for the isolated I2026 market-movement laboratory. The only POST action
/// collects immutable shadow evidence; there is intentionally no publish,
/// promote, stake or productive-settlement action.
/// </summary>
[ApiController]
[Route("api/bot-i2026")]
public sealed class BotI2026Controller : ControllerBase
{
    private readonly SqlAutomationRepository _schemaRepository;
    private readonly IBotIShadowRepository _repository;
    private readonly IBotIShadowCollectorService _collector;
    private readonly ILogger<BotI2026Controller> _logger;

    public BotI2026Controller(
        SqlAutomationRepository schemaRepository,
        IBotIShadowRepository repository,
        IBotIShadowCollectorService collector,
        ILogger<BotI2026Controller> logger)
    {
        _schemaRepository = schemaRepository;
        _repository = repository;
        _collector = collector;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(BotIShadowStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default)
    {
        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _repository.GetStatusAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load I2026 shadow status");
            return Problem(
                title: "I2026 shadow lab is unavailable",
                detail: "The append-only market-movement schema is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("collect")]
    [ProducesResponseType(typeof(BotICollectResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Collect(
        [FromBody] CollectBotI2026Request? request,
        CancellationToken cancellationToken = default)
    {
        var value = request ?? new CollectBotI2026Request();
        var localToday = SantiagoToday();
        var command = new BotICollectCommand(
            value.DateFrom ?? localToday,
            value.DateTo ?? localToday.AddDays(8),
            value.AsOfUtc,
            value.MaximumFixtures);
        try
        {
            BotIShadowLab.Validate(command, DateTime.UtcNow);
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _collector.CollectAsync(command, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "I2026 manual shadow collection failed");
            return Problem(
                title: "I2026 shadow collection failed",
                detail: "No productive pick was created; the collector can be retried safely.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("evaluations")]
    [HttpGet("results")]
    [ProducesResponseType(typeof(BotIEvaluationPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvaluations(
        [FromQuery] DateTime? predictionFromUtc,
        [FromQuery] DateTime? predictionToUtc,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? decision,
        [FromQuery] string? marketType,
        [FromQuery] string? selection,
        [FromQuery] string? source,
        [FromQuery] string? configurationVersion,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new BotIEvaluationFilter(
            predictionFromUtc,
            predictionToUtc,
            asOfUtc,
            decision,
            marketType,
            selection,
            source,
            configurationVersion,
            page,
            pageSize);
        try
        {
            BotIShadowLab.Validate(filter, DateTime.UtcNow);
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _repository.GetEvaluationsAsync(filter, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load I2026 shadow evaluations");
            return Problem(
                title: "Could not load I2026 shadow evaluations",
                detail: "The read-only audit query failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("scorecard")]
    [HttpGet("scorecards")]
    [ProducesResponseType(typeof(IReadOnlyList<BotIShadowScorecardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetScorecards(
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? configurationVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = BotIShadowLab.ValidateAsOf(asOfUtc, DateTime.UtcNow);
            if (configurationVersion is not null
                && (string.IsNullOrWhiteSpace(configurationVersion) || configurationVersion.Trim().Length > 80))
                return BadRequest(new { error = "configurationVersion is invalid." });
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _repository.GetScorecardsAsync(
                asOfUtc,
                configurationVersion,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load I2026 shadow scorecards");
            return Problem(
                title: "Could not load I2026 shadow scorecards",
                detail: "The outcome-aware shadow query failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static DateOnly SantiagoToday()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(id)));
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }
}

public sealed record CollectBotI2026Request(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    DateTime? AsOfUtc = null,
    int MaximumFixtures = 50);
