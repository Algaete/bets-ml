using System.Text.Json;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.Predictions;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

public sealed class PredictionsController : Controller
{
    private readonly MatchHistoryApiClient _matchHistoryApiClient;
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(
        MatchHistoryApiClient matchHistoryApiClient,
        ILogger<PredictionsController> logger)
    {
        _matchHistoryApiClient = matchHistoryApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);

        return View(new PredictionIndexViewModel
        {
            LeagueOptions = leagueOptions,
            FormationOptions = formationOptions
        });
    }

    [HttpPost]
    public async Task<IActionResult> Predict(
        [FromBody] JsonElement features,
        CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            return BadRequest(new { error = "Prediction features payload must be a JSON object." });
        }

        try
        {
            var prediction = await _matchHistoryApiClient.PredictAsync(features, cancellationToken);
            return Ok(prediction);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to request total corners prediction");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Prediction could not be completed. Check that the API and Python model runtime are available." });
        }
    }

    private async Task<IReadOnlyList<string>> LoadLeagueOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetLeagueOptionsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load league dropdown options from backend API");
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> LoadFormationOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetFormationOptionsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load formation dropdown options from backend API");
            return Array.Empty<string>();
        }
    }
}
