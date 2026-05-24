using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.MatchHistory;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

public sealed class MatchHistoryController : Controller
{
    private readonly MatchHistoryApiClient _matchHistoryApiClient;
    private readonly ILogger<MatchHistoryController> _logger;

    public MatchHistoryController(
        MatchHistoryApiClient matchHistoryApiClient,
        ILogger<MatchHistoryController> logger)
    {
        _matchHistoryApiClient = matchHistoryApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);

        return View(new MatchHistoryIndexViewModel
        {
            LeagueOptions = leagueOptions,
            FormationOptions = formationOptions
        });
    }

    [HttpGet]
    public async Task<IActionResult> TeamOptions(
        [FromQuery] string? league,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Json(Array.Empty<Models.Teams.TeamBi3InfoViewModel>());
        }

        var teamOptions = await LoadTeamOptionsAsync(league, cancellationToken);
        return Json(teamOptions);
    }

    [HttpGet]
    public async Task<IActionResult> RecentMatches(
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return Json(Array.Empty<MatchHistoryItemViewModel>());
        }

        var recentMatches = await LoadRecentMatchesAsync(homeTeam, awayTeam, cancellationToken);
        return Json(recentMatches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind(Prefix = "Form")] CreateMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        var recentMatches = await LoadRecentMatchesAsync(
            form.HomeTeam,
            form.AwayTeam,
            cancellationToken);
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var teamOptions = await LoadTeamOptionsAsync(form.League, cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = form,
                RecentMatches = recentMatches,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                TeamOptions = teamOptions
            });
        }

        try
        {
            await _matchHistoryApiClient.CreateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "Match saved successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save match history item");
            ModelState.AddModelError(string.Empty, "The match could not be saved. Check that the API is running.");

            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = form,
                RecentMatches = recentMatches,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                TeamOptions = teamOptions
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        [FromBody] UpdateMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { error = "Invalid match history update payload." });
        }

        try
        {
            await _matchHistoryApiClient.UpdateAsync(form, cancellationToken);
            return Ok(new { message = "Match updated successfully." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update match history item {MatchHistoryId}", form.Id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The match could not be updated. Check that the API is running." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        [FromBody] DeleteMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { error = "Invalid match history delete payload." });
        }

        try
        {
            await _matchHistoryApiClient.DeleteAsync(form.Id, cancellationToken);
            return Ok(new { message = "Match deleted successfully." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete match history item {MatchHistoryId}", form.Id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The match could not be deleted. Check that the API is running." });
        }
    }

    private async Task<IReadOnlyList<MatchHistoryItemViewModel>> LoadRecentMatchesAsync(
        string homeTeam,
        string awayTeam,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetRecentAsync(homeTeam, awayTeam, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load recent matches from backend API");
            return Array.Empty<MatchHistoryItemViewModel>();
        }
    }

    private async Task<IReadOnlyList<string>> LoadLeagueOptionsAsync(
        CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<string>> LoadFormationOptionsAsync(
        CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<Models.Teams.TeamBi3InfoViewModel>> LoadTeamOptionsAsync(
        string league,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetTeamOptionsAsync(league, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load team dropdown options from backend API");
            return Array.Empty<Models.Teams.TeamBi3InfoViewModel>();
        }
    }
}
