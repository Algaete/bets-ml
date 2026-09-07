using CornersPrediction.Application.AutomatedCorners;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/automated-corners")]
public sealed class AutomatedCornersResearchController : ControllerBase
{
    private readonly IGetAutomatedBotResearchEvaluationsUseCase _useCase;
    private readonly ILogger<AutomatedCornersResearchController> _logger;

    public AutomatedCornersResearchController(
        IGetAutomatedBotResearchEvaluationsUseCase useCase,
        ILogger<AutomatedCornersResearchController> logger)
    {
        _useCase = useCase;
        _logger = logger;
    }

    /// <summary>
    /// Returns all audited C/D/E/F/H candidates, including candidates rejected
    /// by the model or blocked from production. This endpoint is read-only.
    /// </summary>
    [HttpGet("research-evaluations")]
    [HttpGet("research/evaluations")]
    [ProducesResponseType(typeof(AutomatedBotResearchPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvaluations(
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? marketFamily = null,
        [FromQuery] string? marketType = null,
        [FromQuery] string? botKey = null,
        [FromQuery] string? modelDecision = null,
        [FromQuery(Name = "decision")] string? decisionAlias = null,
        [FromQuery] string? publicationStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(modelDecision)
                && !string.IsNullOrWhiteSpace(decisionAlias)
                && !modelDecision.Trim().Equals(decisionAlias.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "modelDecision and its legacy alias decision cannot disagree.");
            }

            var filters = new AutomatedBotResearchFilterRequest(
                dateFrom,
                dateTo,
                marketFamily,
                marketType,
                botKey,
                string.IsNullOrWhiteSpace(modelDecision) ? decisionAlias : modelDecision,
                publicationStatus,
                page,
                pageSize);
            return Ok(await _useCase.GetAsync(filters, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to load the automated-bot research audit");
            return Problem(
                title: "Could not load automated-bot research evaluations",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
