using AutomatedCornersBot.Api;
using CornersPrediction.Application.AutomatedCorners;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/automated-corners")]
public sealed class AutomatedCornersController : ControllerBase
{
    private readonly IGetAutomatedCornerSelectionsUseCase _getSelectionsUseCase;
    private readonly IUpdateAutomatedCornerSelectionStatusUseCase _updateSelectionStatusUseCase;
    private readonly AutomatedCornersSelectionService _selectionService;
    private readonly SqlAutomationRepository _automationRepository;
    private readonly ILogger<AutomatedCornersController> _logger;

    public AutomatedCornersController(
        IGetAutomatedCornerSelectionsUseCase getSelectionsUseCase,
        IUpdateAutomatedCornerSelectionStatusUseCase updateSelectionStatusUseCase,
        AutomatedCornersSelectionService selectionService,
        SqlAutomationRepository automationRepository,
        ILogger<AutomatedCornersController> logger)
    {
        _getSelectionsUseCase = getSelectionsUseCase;
        _updateSelectionStatusUseCase = updateSelectionStatusUseCase;
        _selectionService = selectionService;
        _automationRepository = automationRepository;
        _logger = logger;
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

    [HttpPost("settle")]
    [ProducesResponseType(typeof(SettleAutomatedCornersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Settle(
        [FromBody] SettleAutomatedCornersRequest? request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _automationRepository.EnsureSchemaAsync(cancellationToken);
            return Ok(await _selectionService.SettleAsync(request, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to settle automated corners selections");
            return Problem(
                title: "Could not settle automated corners selections",
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
}
