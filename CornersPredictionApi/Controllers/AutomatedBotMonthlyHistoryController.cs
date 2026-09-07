using CornersPrediction.Application.AutomatedCorners;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/automated-corners/monthly-history")]
public sealed class AutomatedBotMonthlyHistoryController(IAutomatedBotMonthlyHistoryRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string marketFamily = "corners",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (dateFrom == default || dateTo == default || dateFrom.Date > dateTo.Date)
                return BadRequest(new { error = "A valid dateFrom/dateTo range is required." });
            return Ok(await repository.GetMonthlyHistoryAsync(dateFrom.Date, dateTo.Date, marketFamily, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
