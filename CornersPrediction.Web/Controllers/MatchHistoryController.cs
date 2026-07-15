using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.MatchHistory;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
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
    public async Task<IActionResult> Index(
        [FromQuery] MatchHistoryFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        filters.Take = filters.Take <= 0 ? 20 : Math.Min(filters.Take, 100);
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);
        var records = await LoadManualEntriesAsync(filters, cancellationToken);

        return View(new MatchHistoryIndexViewModel
        {
            Filters = filters,
            Records = records,
            LeagueOptions = leagueOptions,
            FormationOptions = formationOptions
        });
    }

    [HttpGet]
    public async Task<IActionResult> TeamOptions(
        [FromQuery] string? league,
        [FromQuery] string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Json(Array.Empty<Models.Teams.TeamBi3InfoViewModel>());
        }

        var teamOptions = await LoadTeamOptionsAsync(league, teamGender, cancellationToken);
        return Json(teamOptions);
    }

    [HttpGet]
    public async Task<IActionResult> RecentMatches(
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        [FromQuery] string? league,
        [FromQuery] string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return Json(Array.Empty<MatchHistoryItemViewModel>());
        }

        var recentMatches = await LoadRecentMatchesAsync(homeTeam, awayTeam, league, teamGender, cancellationToken);
        return Json(recentMatches);
    }

    [HttpGet]
    public async Task<IActionResult> PredictionContext(
        [FromQuery] string? league,
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        [FromQuery] string? teamGender,
        [FromQuery] double? baseLocalAwayPrediction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam))
        {
            return Json(null);
        }

        try
        {
            var context = await _matchHistoryApiClient.GetPredictionContextAsync(
                league ?? string.Empty,
                homeTeam,
                awayTeam,
                teamGender,
                baseLocalAwayPrediction,
                cancellationToken);

            return Json(context);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Prediction context request was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load prediction context from backend API");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Prediction context could not be loaded. Check that the API is running." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind(Prefix = "Form")] CreateMatchHistoryViewModel form,
        CancellationToken cancellationToken)
    {
        var filters = new MatchHistoryFiltersViewModel();
        var records = await LoadManualEntriesAsync(filters, cancellationToken);
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var teamOptions = await LoadTeamOptionsAsync(form.League, null, cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = form,
                BulkForm = new BulkMatchHistoryImportViewModel(),
                Filters = filters,
                Records = records,
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
                BulkForm = new BulkMatchHistoryImportViewModel(),
                Filters = filters,
                Records = records,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                TeamOptions = teamOptions
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCreate(
        [Bind(Prefix = "BulkForm")] BulkMatchHistoryImportViewModel form,
        CancellationToken cancellationToken)
    {
        var filters = new MatchHistoryFiltersViewModel
        {
            League = form.League,
            Team = form.FocusTeam,
            Take = 50
        };
        var records = await LoadManualEntriesAsync(filters, cancellationToken);
        var leagueOptions = await LoadLeagueOptionsAsync(cancellationToken);
        var formationOptions = await LoadFormationOptionsAsync(cancellationToken);
        var bulkTeamOptions = await LoadTeamOptionsAsync(form.League, form.TeamGender, cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = new CreateMatchHistoryViewModel(),
                BulkForm = form,
                Filters = filters,
                Records = records,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                BulkTeamOptions = bulkTeamOptions
            });
        }

        try
        {
            var result = await _matchHistoryApiClient.BulkCreateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = $"Bulk import processed: {result.InsertedCount} inserted, {result.DuplicateCount} duplicates, {result.ErrorCount} errors.";

            records = await LoadManualEntriesAsync(filters, cancellationToken);
            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = new CreateMatchHistoryViewModel(),
                BulkForm = form,
                BulkResult = result,
                Filters = filters,
                Records = records,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                BulkTeamOptions = bulkTeamOptions
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to bulk import match history items");
            ModelState.AddModelError("BulkForm.MatchesJson", "The bulk import could not be processed. Check that the API is running and the bulk stored procedure exists.");

            return View("Index", new MatchHistoryIndexViewModel
            {
                Form = new CreateMatchHistoryViewModel(),
                BulkForm = form,
                Filters = filters,
                Records = records,
                LeagueOptions = leagueOptions,
                FormationOptions = formationOptions,
                BulkTeamOptions = bulkTeamOptions
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
        string? league,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetRecentAsync(homeTeam, awayTeam, league, teamGender, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load recent matches from backend API");
            return Array.Empty<MatchHistoryItemViewModel>();
        }
    }

    private async Task<IReadOnlyList<MatchHistoryItemViewModel>> LoadManualEntriesAsync(
        MatchHistoryFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetManualEntriesAsync(filters, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load manually entered matches from backend API");
            return Array.Empty<MatchHistoryItemViewModel>();
        }
    }

    private async Task<IReadOnlyList<string>> LoadLeagueOptionsAsync(
        CancellationToken cancellationToken)
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
        string? teamGender,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _matchHistoryApiClient.GetTeamOptionsAsync(league, teamGender, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load team dropdown options from backend API");
            return Array.Empty<Models.Teams.TeamBi3InfoViewModel>();
        }
    }
}
