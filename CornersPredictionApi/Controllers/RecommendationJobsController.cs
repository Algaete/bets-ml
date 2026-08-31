using CornersPrediction.Application.Automation;
using CornersPredictionApi.Requests;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/recommendation-jobs")]
public sealed class RecommendationJobsController : ControllerBase
{
    private readonly IRecommendationJobsUseCase _useCase;
    private readonly IRecommendationBotDefinitionsUseCase _botDefinitionsUseCase;

    public RecommendationJobsController(
        IRecommendationJobsUseCase useCase,
        IRecommendationBotDefinitionsUseCase botDefinitionsUseCase)
    {
        _useCase = useCase;
        _botDefinitionsUseCase = botDefinitionsUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RecommendationJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Enqueue(
        [FromBody] CreateRecommendationJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await _useCase.EnqueueAsync(
                new CreateRecommendationJobCommand(
                    request.DateFrom,
                    request.DateTo,
                    request.Name,
                    request.BotKeys,
                    request.MarketFamilies,
                    request.Mode,
                    request.BatchSize,
                    request.MaxAttempts),
                cancellationToken);
            return AcceptedAtAction(nameof(Get), new { jobId = job.RecommendationJobId }, job);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RecommendationJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _useCase.ListAsync(take, cancellationToken));

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType(typeof(RecommendationJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _useCase.GetAsync(jobId, cancellationToken);
        return job is null ? NotFound(new { error = $"Recommendation job {jobId} was not found." }) : Ok(job);
    }

    [HttpDelete("{jobId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var cancelled = await _useCase.CancelAsync(jobId, cancellationToken);
        return cancelled ? NoContent() : NotFound(new { error = "The job is missing or already finished." });
    }

    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken cancellationToken)
    {
        var definitions = await _botDefinitionsUseCase.GetAllAsync(cancellationToken);
        return Ok(new
        {
            botKeys = definitions
                .Where(bot => bot.IsEnabled && !RecommendationBotLifecycle.IsRetired(bot.BotKey))
                .Select(bot => bot.BotKey),
            marketFamilies = new[] { "CORNERS", "GOALS", "SHOTS", "SOG" },
            modes = new[] { RecommendationJobModes.HistoricalBackfill, RecommendationJobModes.Live },
            persistence = "Azure SQL",
            resumable = true,
            idempotent = true
        });
    }
}
