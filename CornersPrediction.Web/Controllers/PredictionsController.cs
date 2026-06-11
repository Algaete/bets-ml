using System.Text.Json;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.MatchHistory;
using CornersPrediction.Web.Models.Predictions;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
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
    public async Task<IActionResult> Index(
        string? homeTeam,
        string? awayTeam,
        string? league,
        string? season,
        DateTime? matchDate,
        bool? isKnockout,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);

        return View(new PredictionIndexViewModel
        {
            LeagueOptions = leagueOptions,
            FormationOptions = formationOptions,
            League = NormalizeText(league),
            Season = NormalizeText(season) ?? matchDate?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MatchDate = matchDate,
            HomeTeam = NormalizeText(homeTeam),
            AwayTeam = NormalizeText(awayTeam),
            TeamGender = NormalizeTeamGender(teamGender),
            IsKnockout = isKnockout ?? false
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

    [HttpPost]
    public async Task<IActionResult> PredictOverUnder(
        [FromBody] JsonElement features,
        CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            return BadRequest(new { error = "Over/Under prediction features payload must be a JSON object." });
        }

        try
        {
            var prediction = await _matchHistoryApiClient.PredictOverUnderAsync(features, cancellationToken);
            return Ok(prediction);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to request Over/Under prediction");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Total corners predicted, but Over/Under model could not be executed." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> PredictShotsOnGoal(
        [FromBody] JsonElement features,
        CancellationToken cancellationToken)
    {
        return await PredictShotsAndSogAsync(features, cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> PredictShots(
        [FromBody] JsonElement features,
        CancellationToken cancellationToken)
    {
        return await PredictShotsAndSogAsync(features, cancellationToken);
    }

    private async Task<IActionResult> PredictShotsAndSogAsync(
        JsonElement features,
        CancellationToken cancellationToken)
    {
        if (features.ValueKind is not JsonValueKind.Object)
        {
            return BadRequest(new { error = "Shots and SOG prediction features payload must be a JSON object." });
        }

        try
        {
            var prediction = await _matchHistoryApiClient.PredictShotsOnGoalAsync(features, cancellationToken);
            return Ok(prediction);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to request shots and SOG prediction");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Corners predicted, but shots and SOG models could not be executed." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> LatestFormation(
        [FromQuery] string? league,
        [FromQuery] string? team,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(team))
        {
            return Ok(new { formation = (string?)null });
        }

        try
        {
            var matches = await _matchHistoryApiClient.GetManualEntriesAsync(
                new MatchHistoryFiltersViewModel
                {
                    League = null,
                    Team = team.Trim(),
                    Take = 50
                },
                cancellationToken);

            var latestFormation = FindLatestFormation(matches, team);
            return Ok(latestFormation);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load latest formation for {Team}", team);
            return Ok(new { formation = (string?)null });
        }
    }

    private async Task<IReadOnlyList<string>> LoadLeagueOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetLeagueOptionsAsync(null, cancellationToken);
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

    private static object FindLatestFormation(
        IReadOnlyList<MatchHistoryItemViewModel> matches,
        string team)
    {
        var normalizedTeam = Normalize(team);
        var orderedMatches = matches
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .ToArray();

        foreach (var match in orderedMatches)
        {
            if (Normalize(match.HomeTeam) == normalizedTeam && IsKnownFormation(match.HomeFormation))
            {
                return new
                {
                    formation = match.HomeFormation,
                    source = "home",
                    matchDate = match.MatchDate.ToString("yyyy-MM-dd"),
                    opponent = match.AwayTeam
                };
            }

            if (Normalize(match.AwayTeam) == normalizedTeam && IsKnownFormation(match.AwayFormation))
            {
                return new
                {
                    formation = match.AwayFormation,
                    source = "away",
                    matchDate = match.MatchDate.ToString("yyyy-MM-dd"),
                    opponent = match.HomeTeam
                };
            }
        }

        var fallback = orderedMatches.FirstOrDefault(match =>
            IsKnownFormation(match.HomeFormation) || IsKnownFormation(match.AwayFormation));

        if (fallback is null)
        {
            return new { formation = (string?)null };
        }

        var formation = IsKnownFormation(fallback.HomeFormation)
            ? fallback.HomeFormation
            : fallback.AwayFormation;

        return new
        {
            formation,
            source = "fallback",
            matchDate = fallback.MatchDate.ToString("yyyy-MM-dd"),
            opponent = Normalize(fallback.HomeTeam) == normalizedTeam ? fallback.AwayTeam : fallback.HomeTeam
        };
    }

    private static bool IsKnownFormation(string? formation)
    {
        if (string.IsNullOrWhiteSpace(formation))
        {
            return false;
        }

        return !formation.Trim().Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            && !formation.Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeTeamGender(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "F" ? "F" : "M";
    }
}
