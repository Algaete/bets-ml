using CornersPrediction.Application.Automation;
using CornersPredictionApi.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/corners-pipeline")]
public sealed class CornersPipelineController : ControllerBase
{
    private readonly ICornersPipelineService _cornersPipelineService;

    public CornersPipelineController(ICornersPipelineService cornersPipelineService)
    {
        _cornersPipelineService = cornersPipelineService;
    }

    [HttpPost("match-history")]
    [ProducesResponseType(typeof(CornersPipelineStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunMatchHistory(
        [FromBody] RunPipelineStepRequest? request,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.RunMatchHistoryAsync(request?.Days ?? 7, cancellationToken));
    }

    [HttpPost("upcoming-matches")]
    [ProducesResponseType(typeof(CornersPipelineStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunUpcomingMatches(
        [FromBody] RunPipelineStepRequest? request,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.RunUpcomingMatchesAsync(request?.Days ?? 7, cancellationToken));
    }

    [HttpPost("pinnacle-odds")]
    [ProducesResponseType(typeof(CornersPipelineStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunPinnacleOdds(CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.RunPinnacleOddsAsync(cancellationToken));
    }

    [HttpPost("betano-odds")]
    [ProducesResponseType(typeof(CornersPipelineStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunBetanoOdds(CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.RunBetanoOddsAsync(cancellationToken));
    }

    [HttpPost("bots")]
    [ProducesResponseType(typeof(CornersPipelineStepResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunBots(
        [FromBody] RunBotsRequest? request,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.RunBotsAsync(
            new RunBotsCommand(
                ExcludeExistingSelections: request?.ExcludeExistingSelections ?? false,
                BatchNumber: request?.BatchNumber ?? 1,
                BatchSize: request?.BatchSize ?? 100,
                RunBotC: request?.RunBotC ?? true),
            cancellationToken));
    }

    [HttpGet("bots/availability")]
    [ProducesResponseType(typeof(BotOddsAvailability), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBotOddsAvailability(
        [FromQuery] int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _cornersPipelineService.GetBotOddsAvailabilityAsync(batchSize, cancellationToken));
    }

    [HttpPost("full-run")]
    [ProducesResponseType(typeof(CornersPipelineRunResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> RunFullPipeline(
        [FromBody] RunFullPipelineRequest? request,
        CancellationToken cancellationToken = default)
    {
        var command = new RunFullPipelineCommand(
            MatchHistoryDays: request?.MatchHistoryDays ?? 7,
            UpcomingDays: request?.UpcomingDays ?? 7,
            ExcludeExistingSelections: request?.ExcludeExistingSelections ?? false,
            BotBatchNumber: request?.BotBatchNumber ?? 1,
            BotBatchSize: request?.BotBatchSize ?? 100,
            RunBotC: request?.RunBotC ?? true);

        return Ok(await _cornersPipelineService.RunFullPipelineAsync(command, cancellationToken));
    }
}
