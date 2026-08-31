using CornersPrediction.Application.Automation.BotH;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

/// <summary>
/// Read-only surface for the H2026 shadow experiment.  This controller intentionally
/// has no run, publish, promote or settlement-mutation endpoint.
/// </summary>
[ApiController]
[Route("api/bot-h2026")]
public sealed class BotH2026Controller : ControllerBase
{
    private readonly IBotHShadowLabReadRepository _repository;
    private readonly ILogger<BotH2026Controller> _logger;

    public BotH2026Controller(
        IBotHShadowLabReadRepository repository,
        ILogger<BotH2026Controller> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(BotHShadowLabStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _repository.GetStatusAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load the Bot H2026 shadow-lab status");
            return Problem(
                title: "Bot H2026 shadow lab is unavailable",
                detail: "The read-only shadow-lab schema or data source is unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("evaluations")]
    [HttpGet("results")]
    [ProducesResponseType(typeof(BotHShadowEvaluationPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvaluations(
        [FromQuery] DateTime? predictionFromUtc,
        [FromQuery] DateTime? predictionToUtc,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? decision,
        [FromQuery] string? marketType,
        [FromQuery] string? selection,
        [FromQuery] string? configurationVersion,
        [FromQuery] string? settlementState,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = new BotHShadowEvaluationFilter(
            predictionFromUtc,
            predictionToUtc,
            asOfUtc,
            decision,
            marketType,
            selection,
            configurationVersion,
            settlementState,
            page,
            pageSize);

        try
        {
            BotHShadowLab.Validate(filter, DateTime.UtcNow);
            return Ok(await _repository.GetEvaluationsAsync(filter, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot H2026 shadow evaluations");
            return Problem(
                title: "Could not load Bot H2026 shadow evaluations",
                detail: "The read-only shadow-lab query failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("scorecards")]
    [HttpGet("scorecard")]
    [ProducesResponseType(typeof(IReadOnlyList<BotHShadowScorecardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetScorecards(
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? configurationVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = new BotHShadowScorecardFilter(asOfUtc, configurationVersion);
        try
        {
            BotHShadowLab.Validate(filter, DateTime.UtcNow);
            return Ok(await _repository.GetScorecardsAsync(filter, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot H2026 shadow scorecards");
            return Problem(
                title: "Could not load Bot H2026 shadow scorecards",
                detail: "The read-only shadow-lab query failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("threshold-analysis")]
    [HttpGet("what-if")]
    [ProducesResponseType(typeof(IReadOnlyList<BotHThresholdAnalysisDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetThresholdAnalysis(
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] string? configurationVersion,
        [FromQuery] string? marketType,
        [FromQuery] string? selection,
        [FromQuery] string analysisVersion = BotHShadowLab.ThresholdAnalysisVersion,
        [FromQuery] decimal minimumFinalProbability = 0.56m,
        [FromQuery] decimal minimumFinalEdge = 0.04m,
        [FromQuery] decimal minimumFinalExpectedValue = 0.03m,
        [FromQuery] decimal minimumDataQualityScore = 0.70m,
        [FromQuery] decimal minimumContextAgreementScore = 0.70m,
        [FromQuery] decimal minimumOdds = 1.60m,
        [FromQuery] decimal maximumOdds = 2.20m,
        [FromQuery] decimal developmentFraction = 0.70m,
        CancellationToken cancellationToken = default)
    {
        var filter = new BotHThresholdAnalysisFilter(
            asOfUtc,
            configurationVersion,
            marketType,
            selection,
            analysisVersion,
            minimumFinalProbability,
            minimumFinalEdge,
            minimumFinalExpectedValue,
            minimumDataQualityScore,
            minimumContextAgreementScore,
            minimumOdds,
            maximumOdds,
            developmentFraction);

        try
        {
            BotHShadowLab.Validate(filter, DateTime.UtcNow);
            return Ok(await _repository.GetThresholdAnalysisAsync(filter, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot H2026 threshold analysis");
            return Problem(
                title: "Could not load Bot H2026 threshold analysis",
                detail: "The versioned, read-only threshold replay failed.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
