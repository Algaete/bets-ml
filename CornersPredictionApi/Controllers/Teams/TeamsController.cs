using CornersPrediction.Application.Teams;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers.Teams;

/// <summary>
/// Backend endpoints for team metadata loaded from SQL Server stored procedures.
/// </summary>
[ApiController]
[Route("api/teams")]
public sealed class TeamsController : ControllerBase
{
    private readonly IGetTeamBi3InfoUseCase _getTeamBi3InfoUseCase;
    private readonly IGetTeamBig3LeaguesUseCase _getTeamBig3LeaguesUseCase;
    private readonly IGetFormationListUseCase _getFormationListUseCase;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(
        IGetTeamBi3InfoUseCase getTeamBi3InfoUseCase,
        IGetTeamBig3LeaguesUseCase getTeamBig3LeaguesUseCase,
        IGetFormationListUseCase getFormationListUseCase,
        ILogger<TeamsController> logger)
    {
        _getTeamBi3InfoUseCase = getTeamBi3InfoUseCase;
        _getTeamBig3LeaguesUseCase = getTeamBig3LeaguesUseCase;
        _getFormationListUseCase = getFormationListUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Calls sp_GetMatchHistoryLeagues and returns the available standardized leagues.
    /// </summary>
    [HttpGet("big3-leagues")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBig3Leagues(
        [FromQuery] string? teamGender,
        CancellationToken cancellationToken)
    {
        try
        {
            var leagues = await _getTeamBig3LeaguesUseCase.GetAsync(teamGender, cancellationToken);
            return Ok(leagues);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load big3 leagues from SQL Server");
            return Problem(
                title: "Could not load leagues",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Calls sp_GetTeamsByLeague with a League parameter and returns standardized team values.
    /// </summary>
    [HttpGet("big3-info")]
    [HttpGet("bi3-info")]
    [ProducesResponseType(typeof(IReadOnlyList<TeamBi3InfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBi3Info(
        [FromQuery] string? league,
        [FromQuery] string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return BadRequest(new { message = "Query parameter 'league' is required." });
        }

        try
        {
            var teams = await _getTeamBi3InfoUseCase.GetAsync(league, teamGender, cancellationToken);
            return Ok(teams);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load team big3 info from SQL Server");
            return Problem(
                title: "Could not load team metadata",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Calls sp_GetFormationList and returns available formation values.
    /// </summary>
    [HttpGet("formations")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFormations(CancellationToken cancellationToken)
    {
        try
        {
            var formations = await _getFormationListUseCase.GetAsync(cancellationToken);
            return Ok(formations);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load formations from SQL Server");
            return Problem(
                title: "Could not load formations",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
