using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
public sealed class BotPicksController : Controller
{
    private readonly AutomatedCornersApiClient _automatedCornersApiClient;
    private readonly ILogger<BotPicksController> _logger;

    public BotPicksController(
        AutomatedCornersApiClient automatedCornersApiClient,
        ILogger<BotPicksController> logger)
    {
        _automatedCornersApiClient = automatedCornersApiClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index([FromQuery] BotPickFiltersViewModel filters)
    {
        return View(new BotPicksIndexViewModel { Filters = filters });
    }

    [HttpGet]
    public async Task<IActionResult> Selections(
        [FromQuery] BotPickFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        try
        {
            var selections = await _automatedCornersApiClient.GetSelectionsAsync(filters, cancellationToken);
            return Json(selections);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Bot picks request was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load bot picks from backend API");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Bot picks could not be loaded. Check that the API and stored procedure are available." });
        }
    }

    [HttpPut]
    [Authorize(Policy = PlatformPolicies.Admin)]
    public async Task<IActionResult> Status(
        [FromQuery] long id,
        [FromBody] UpdateBotPickStatusViewModel request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedSelection = await _automatedCornersApiClient.UpdateSelectionStatusAsync(id, request, cancellationToken);
            return Json(updatedSelection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Bot pick status update was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not update bot pick {SelectionId} status", id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Bot pick status could not be updated. Check that the API and stored procedure are available." });
        }
    }
}
