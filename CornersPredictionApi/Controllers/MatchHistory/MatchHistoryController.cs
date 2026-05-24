using CornersPrediction.Application.MatchHistory;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers.MatchHistory;

/// <summary>
/// Backend endpoints for storing raw historical match statistics.
/// </summary>
[ApiController]
[Route("api/matches")]
public sealed class MatchHistoryController : ControllerBase
{
    private readonly ICreateMatchHistoryItemUseCase _createMatchHistoryItemUseCase;
    private readonly IUpdateMatchHistoryItemUseCase _updateMatchHistoryItemUseCase;
    private readonly IDeleteMatchHistoryItemUseCase _deleteMatchHistoryItemUseCase;
    private readonly IGetRecentMatchHistoryUseCase _getRecentMatchHistoryUseCase;
    private readonly ILogger<MatchHistoryController> _logger;

    public MatchHistoryController(
        ICreateMatchHistoryItemUseCase createMatchHistoryItemUseCase,
        IUpdateMatchHistoryItemUseCase updateMatchHistoryItemUseCase,
        IDeleteMatchHistoryItemUseCase deleteMatchHistoryItemUseCase,
        IGetRecentMatchHistoryUseCase getRecentMatchHistoryUseCase,
        ILogger<MatchHistoryController> logger)
    {
        _createMatchHistoryItemUseCase = createMatchHistoryItemUseCase;
        _updateMatchHistoryItemUseCase = updateMatchHistoryItemUseCase;
        _deleteMatchHistoryItemUseCase = deleteMatchHistoryItemUseCase;
        _getRecentMatchHistoryUseCase = getRecentMatchHistoryUseCase;
        _logger = logger;
    }

    /// <summary>
    /// Returns the last 10 home matches for the selected home team and the last 10 away matches for the selected away team.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MatchHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRecent(
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return BadRequest(new { error = "homeTeam and awayTeam query parameters are required." });
        }

        try
        {
            var matches = await _getRecentMatchHistoryUseCase.GetAsync(homeTeam, awayTeam, cancellationToken);
            return Ok(matches);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load recent match history items");
            return Problem(
                title: "Could not load match history",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Stores one completed match with raw stats such as corners, goals, shots and possession.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MatchHistoryItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMatchHistoryItemCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var created = await _createMatchHistoryItemUseCase.CreateAsync(command, cancellationToken);
            return Created($"/api/matches/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save match history item");
            return Problem(
                title: "Could not save match",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Updates a historical match record by id.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        [FromRoute] int id,
        [FromBody] UpdateMatchHistoryItemCommand? command,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var rowsAffected = await _updateMatchHistoryItemUseCase.UpdateAsync(id, command, cancellationToken);
            return rowsAffected == 0 ? NotFound(new { error = "Match history item was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update match history item {MatchHistoryId}", id);
            return Problem(
                title: "Could not update match",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Deletes a historical match record by id.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] int id,
        CancellationToken cancellationToken)
    {
        try
        {
            var rowsAffected = await _deleteMatchHistoryItemUseCase.DeleteAsync(id, cancellationToken);
            return rowsAffected == 0 ? NotFound(new { error = "Match history item was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete match history item {MatchHistoryId}", id);
            return Problem(
                title: "Could not delete match",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
