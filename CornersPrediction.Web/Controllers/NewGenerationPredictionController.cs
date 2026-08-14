using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.NewGeneration;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
[Route("modelos-2026")]
[Route("corners-ml")]
public sealed class NewGenerationPredictionController : Controller
{
    private readonly NewGenerationPredictionApiClient _newGeneration;
    private readonly MatchHistoryApiClient _matchHistory;
    private readonly ILogger<NewGenerationPredictionController> _logger;

    public NewGenerationPredictionController(
        NewGenerationPredictionApiClient newGeneration,
        MatchHistoryApiClient matchHistory,
        ILogger<NewGenerationPredictionController> logger)
    {
        _newGeneration = newGeneration;
        _matchHistory = matchHistory;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? homeTeam,
        string? awayTeam,
        string? league,
        string? season,
        DateTime? matchDate,
        bool? isKnockout,
        CancellationToken cancellationToken)
    {
        var catalogTask = SafeModelCatalogAsync(cancellationToken);
        var leaguesTask = SafeLeaguesAsync(cancellationToken);
        var formationsTask = SafeFormationsAsync(cancellationToken);
        await Task.WhenAll(catalogTask, leaguesTask, formationsTask);
        return View(new NewGenerationIndexViewModel
        {
            ModelCatalog = await catalogTask,
            LeagueOptions = await leaguesTask,
            FormationOptions = await formationsTask,
            League = Normalize(league),
            Season = Normalize(season) ?? matchDate?.Year.ToString(),
            MatchDate = matchDate,
            HomeTeam = Normalize(homeTeam),
            AwayTeam = Normalize(awayTeam),
            IsKnockout = isKnockout ?? false
        });
    }

    [HttpGet("model-info")]
    public async Task<IActionResult> ModelInfo(CancellationToken cancellationToken) =>
        Ok(await SafeModelCatalogAsync(cancellationToken));

    [HttpPost("predict")]
    public async Task<IActionResult> Predict(
        [FromBody] NewGenerationPredictViewModel? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Debes indicar el partido." });
        }
        try
        {
            return Ok(await _newGeneration.PredictAllAsync(request, cancellationToken));
        }
        catch (NewGenerationApiException exception)
        {
            return StatusCode((int)exception.StatusCode, new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not complete new-generation prediction");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "No se pudo completar la predicción. Revisa el estado del modelo y la API."
            });
        }
    }

    private async Task<NewGenerationModelCatalogViewModel> SafeModelCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _newGeneration.GetModelCatalogAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load new-generation model info");
            return new NewGenerationModelCatalogViewModel
            {
                Status = "unavailable",
                Error = "La API no está disponible para consultar el modelo."
            };
        }
    }

    private async Task<IReadOnlyList<string>> SafeLeaguesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistory.GetLeagueOptionsAsync("M", cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load leagues for new-generation prediction page");
            return Array.Empty<string>();
        }
    }

    private async Task<IReadOnlyList<string>> SafeFormationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistory.GetFormationOptionsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load formations for new-generation prediction page");
            return Array.Empty<string>();
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
