using CornersPrediction.Application.UpcomingMatches;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/upcoming-matches")]
public sealed class UpcomingMatchesController : ControllerBase
{
    private readonly IGetUpcomingMatchesUseCase _getUpcomingMatchesUseCase;
    private readonly ILogger<UpcomingMatchesController> _logger;

    public UpcomingMatchesController(
        IGetUpcomingMatchesUseCase getUpcomingMatchesUseCase,
        ILogger<UpcomingMatchesController> logger)
    {
        _getUpcomingMatchesUseCase = getUpcomingMatchesUseCase;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UpcomingMatchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNextWeek(
        [FromQuery] string? genero,
        [FromQuery] string? liga,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var matches = await _getUpcomingMatchesUseCase.GetAsync(
                genero,
                liga,
                cancellationToken);

            return Ok(matches);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load upcoming matches");
            return Problem(
                title: "Could not load upcoming matches",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
