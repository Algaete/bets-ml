using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotAutomation;
using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
public sealed class BotPicksController : Controller
{
    private readonly AutomatedCornersApiClient _automatedCornersApiClient;
    private readonly RecommendationAutomationApiClient _recommendationAutomationApiClient;
    private readonly ILogger<BotPicksController> _logger;

    public BotPicksController(
        AutomatedCornersApiClient automatedCornersApiClient,
        RecommendationAutomationApiClient recommendationAutomationApiClient,
        ILogger<BotPicksController> logger)
    {
        _automatedCornersApiClient = automatedCornersApiClient;
        _recommendationAutomationApiClient = recommendationAutomationApiClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index(
        [FromQuery] BotPickFiltersViewModel filters,
        [FromQuery] string market = "corners")
        => MarketPage(filters, ResolveMarket(market));

    // Compatibility routes: every market now has one canonical Bot Picks page.
    [HttpGet]
    public IActionResult Goals([FromQuery] BotPickFiltersViewModel filters)
        => RedirectToMarket(filters, "goals");

    [HttpGet]
    public IActionResult ShotsOnGoal([FromQuery] BotPickFiltersViewModel filters)
        => RedirectToMarket(filters, "sog");

    [HttpGet]
    public IActionResult Shots([FromQuery] BotPickFiltersViewModel filters)
        => RedirectToMarket(filters, "shots");

    private IActionResult MarketPage(BotPickFiltersViewModel filters, BotPickMarketPageViewModel market)
    {
        ApplyDefaultMonthRange(filters);
        return View("Index", new BotPicksIndexViewModel { Filters = filters, Market = market });
    }

    private IActionResult RedirectToMarket(BotPickFiltersViewModel filters, string market)
    {
        return RedirectToAction(nameof(Index), new
        {
            market,
            filters.DateFrom,
            filters.DateTo,
            filters.Status,
            filters.League,
            filters.Bookmaker,
            filters.MarketType,
            filters.OnlyPending
        });
    }

    [HttpGet]
    public async Task<IActionResult> Selections(
        [FromQuery] BotPickFiltersViewModel filters,
        [FromQuery] string marketFamily = "corners",
        CancellationToken cancellationToken = default)
    {
        try
        {
            ApplyDefaultMonthRange(filters);
            var selectionsTask = _automatedCornersApiClient.GetSelectionsAsync(filters, cancellationToken);
            var definitionsTask = _recommendationAutomationApiClient.GetBotsAsync(cancellationToken);
            var performanceTask = _automatedCornersApiClient.GetPerformanceScorecardsAsync(cancellationToken);
            var selections = FilterVisibleBots(FilterMarketFamily(
                await selectionsTask,
                marketFamily));
            IReadOnlyList<RecommendationBotDefinitionViewModel> definitions;
            IReadOnlyList<BotPerformanceScorecardViewModel> performance;
            try
            {
                definitions = await definitionsTask;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Bot Picks loaded without bot definitions; the productive plan will fail closed to monitoring");
                definitions = [];
            }

            try
            {
                performance = await performanceTask;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Bot Picks loaded without server performance gates");
                performance = [];
            }

            BotPickProductionPlanner.Apply(selections, definitions, marketFamily, performance);
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

    [HttpGet]
    public async Task<IActionResult> PerformanceScorecards(
        [FromQuery] string marketFamily = "corners",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var family = marketFamily.Trim().ToUpperInvariant() switch
            {
                "GOALS" => "GOALS",
                "SHOTS" => "SHOTS",
                "SOG" => "SOG",
                _ => "CORNERS"
            };
            var rows = await _automatedCornersApiClient.GetPerformanceScorecardsAsync(cancellationToken);
            return Json(rows.Where(row => row.MarketFamily == family
                && (row.Dimension == "BotFamily"
                    || row.Dimension == "BotMarketType"
                    || row.Dimension == "BotMarketSideBookmakerVersion")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load server bot performance scorecards");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "No se pudo cargar el semáforo de rendimiento." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> MonthlyHistory(
        [FromQuery] string marketFamily = "corners",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var selections = await _automatedCornersApiClient.GetSelectionsAsync(
                new BotPickFiltersViewModel
                {
                    DateFrom = currentMonth.AddMonths(-11),
                    DateTo = DateTime.Today
                },
                cancellationToken);

            var summaries = FilterVisibleBots(FilterMarketFamily(selections, marketFamily))
                .GroupBy(selection => new
                {
                    Month = new DateTime(selection.MatchDate.Year, selection.MatchDate.Month, 1),
                    BotKey = ResolveBotKey(selection)
                })
                .OrderByDescending(group => group.Key.Month)
                .ThenBy(group => BotSortOrder(group.Key.BotKey))
                .Select(group =>
                {
                    var settled = group
                        .Where(selection =>
                            string.Equals(selection.Status, "Won", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(selection.Status, "Lost", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(selection.Status, "Push", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var settledStake = settled.Sum(selection => selection.Stake);
                    var profitLoss = group.Sum(selection => selection.ProfitLoss ?? 0m);

                    return new BotPickMonthlySummaryViewModel
                    {
                        Month = group.Key.Month,
                        BotKey = group.Key.BotKey,
                        BotLabel = ResolveBotLabel(group.Key.BotKey),
                        Total = group.Count(),
                        Pending = group.Count(selection => string.Equals(selection.Status, "Pending", StringComparison.OrdinalIgnoreCase)),
                        Won = group.Count(selection => string.Equals(selection.Status, "Won", StringComparison.OrdinalIgnoreCase)),
                        Lost = group.Count(selection => string.Equals(selection.Status, "Lost", StringComparison.OrdinalIgnoreCase)),
                        Push = group.Count(selection => string.Equals(selection.Status, "Push", StringComparison.OrdinalIgnoreCase)),
                        Void = group.Count(selection => string.Equals(selection.Status, "Void", StringComparison.OrdinalIgnoreCase)),
                        ProfitLoss = profitLoss,
                        SettledStake = settledStake,
                        YieldPct = settledStake > 0 ? profitLoss / settledStake * 100m : null
                    };
                })
                .ToArray();

            return Json(summaries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Monthly bot history request was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load monthly bot history");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Monthly bot history could not be loaded." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Intelligence(
        [FromQuery] long fixtureId,
        [FromQuery] DateTime? cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        if (fixtureId <= 0)
        {
            return BadRequest(new { error = "FixtureId inválido." });
        }

        try
        {
            return Json(await _automatedCornersApiClient.GetFootballIntelligenceAsync(
                fixtureId,
                cutoffUtc,
                cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "La consulta de inteligencia fue cancelada." });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not load pre-match intelligence for FixtureId={FixtureId}",
                fixtureId);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "No se pudo cargar la inteligencia prepartido." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> RobustEvaluation(
        [FromQuery] long id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest(new { error = "SelectionId inválido." });
        }

        try
        {
            var detail = await _automatedCornersApiClient.GetRobustEvaluationAsync(id, cancellationToken);
            return detail is null
                ? NotFound(new
                {
                    available = false,
                    message = "Este pick todavía no tiene una evaluación robusta. Los picks históricos siguen disponibles sin ella."
                })
                : Json(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "La consulta de evaluación robusta fue cancelada." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load robust evaluation for Bot Pick {SelectionId}", id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "No se pudo cargar la evaluación robusta desde la API." });
        }
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
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

    [HttpPut]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PlatformPolicies.Admin)]
    public async Task<IActionResult> Resolve(
        [FromQuery] long id,
        [FromBody] ResolveBotPickViewModel request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedSelection = await _automatedCornersApiClient.ResolveSelectionAsync(id, request, cancellationToken);
            return Json(updatedSelection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Bot pick settlement was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not resolve bot pick {SelectionId}", id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "No se pudo liquidar el pick. Revisa que la API y el procedimiento almacenado estén disponibles." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PlatformPolicies.Admin)]
    public async Task<IActionResult> SettlePending(
        [FromBody] SettlePendingBotPicksViewModel request,
        CancellationToken cancellationToken)
    {
        try
        {
            request.MaxRows = Math.Clamp(request.MaxRows, 1, 20000);
            var settlement = await _automatedCornersApiClient.SettlePendingAsync(request, cancellationToken);
            return Json(settlement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "La liquidación de picks pendientes fue cancelada." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not settle pending Bot Picks from local MatchHistory");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "No se pudieron liquidar los pendientes desde MatchHistory." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PlatformPolicies.Admin)]
    public async Task<IActionResult> ReconcileAvailable(
        [FromBody] ReconcileAvailableBotPicksViewModel request,
        CancellationToken cancellationToken)
    {
        try
        {
            request.MaxSelections = Math.Clamp(request.MaxSelections, 1, 20000);
            request.DryRun = false;
            var result = await _automatedCornersApiClient.ReconcileAvailableAsync(request, cancellationToken);
            return Json(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "La sincronización y liquidación fue cancelada." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reconcile all Bot Picks");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "No se pudieron sincronizar y liquidar los Bot Picks disponibles." });
        }
    }

    [HttpDelete]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PlatformPolicies.Admin)]
    public async Task<IActionResult> Delete(
        [FromQuery] long id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _automatedCornersApiClient.DeleteSelectionAsync(id, cancellationToken);
            return NoContent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Bot pick deletion was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not delete bot pick {SelectionId}", id);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "Bot pick could not be deleted. Check that the API and stored procedure are available." });
        }
    }

    private static void ApplyDefaultMonthRange(BotPickFiltersViewModel filters)
    {
        var referenceDate = filters.DateFrom ?? filters.DateTo ?? DateTime.Today;
        var monthStart = new DateTime(referenceDate.Year, referenceDate.Month, 1);
        filters.DateFrom ??= monthStart;
        filters.DateTo ??= monthStart.AddMonths(1).AddDays(-1);
    }

    private static IReadOnlyList<BotPickSelectionViewModel> FilterMarketFamily(
        IReadOnlyList<BotPickSelectionViewModel> selections,
        string marketFamily)
    {
        var allowed = ResolveMarket(marketFamily).Options
            .Select(option => option.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selections.Where(selection => allowed.Contains(selection.MarketType)).ToArray();
    }

    private static IReadOnlyList<BotPickSelectionViewModel> FilterVisibleBots(
        IReadOnlyList<BotPickSelectionViewModel> selections)
        => selections
            .Where(selection => !string.Equals(
                ResolveBotKey(selection),
                "B",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string ResolveBotKey(BotPickSelectionViewModel selection)
    {
        var automationVersion = selection.AutomationVersion.Trim();
        if (automationVersion.EndsWith("-A", StringComparison.OrdinalIgnoreCase))
        {
            return "A";
        }

        if (automationVersion.EndsWith("-B", StringComparison.OrdinalIgnoreCase))
        {
            return "B";
        }

        if (automationVersion.EndsWith("-C2026", StringComparison.OrdinalIgnoreCase))
        {
            return "C";
        }

        if (automationVersion.EndsWith("-D2026", StringComparison.OrdinalIgnoreCase))
        {
            return "D";
        }

        if (automationVersion.EndsWith("-E2026", StringComparison.OrdinalIgnoreCase))
        {
            return "E";
        }
        if (automationVersion.EndsWith("-F2026", StringComparison.OrdinalIgnoreCase))
        {
            return "F";
        }

        if (!string.IsNullOrWhiteSpace(selection.DecisionReason))
        {
            try
            {
                using var decision = JsonDocument.Parse(selection.DecisionReason);
                if (decision.RootElement.TryGetProperty("botProfile", out var profile))
                {
                    return profile.GetString()?.Trim().ToUpperInvariant() switch
                    {
                        "A" => "A",
                        "B" => "B",
                        "C" or "C2026" => "C",
                        "D" or "D2026" => "D",
                        "E" or "E2026" => "E",
                        "F" or "F2026" => "F",
                        _ => "Legacy"
                    };
                }
            }
            catch (JsonException)
            {
                // Legacy rows may contain a free-text decision reason.
            }
        }

        // Rows created before bot profiles existed belong to the original Bot A strategy.
        return "A";
    }

    private static string ResolveBotLabel(string botKey) => botKey switch
    {
        "A" => "Bot A Actual",
        "B" => "Bot B · Retirado",
        "C" => "Bot C · Modelos 2026",
        "D" => "Bot D · Team Strength Gap",
        "E" => "Bot E · Calibración empírica",
        "F" => "Bot F · Legacy ML calibrado",
        _ => "Bots personalizados"
    };

    private static int BotSortOrder(string botKey) => botKey switch
    {
        "A" => 1,
        "B" => 2,
        "C" => 3,
        "D" => 4,
        "E" => 5,
        "F" => 6,
        _ => 7
    };

    private static BotPickMarketPageViewModel ResolveMarket(string? marketFamily) =>
        marketFamily?.Trim().ToLowerInvariant() switch
        {
            "goals" => GoalsMarket,
            "shots" => ShotsMarket,
            "sog" or "shots-on-goal" => ShotsOnGoalMarket,
            _ => CornersMarket
        };

    private static readonly BotPickMarketPageViewModel CornersMarket = new(
        "corners",
        "Bot Picks · Córners",
        "Robot automático de córners",
        "Recomendaciones de córners con edge, valor esperado, score y estadísticas de rendimiento.",
        "córners",
        new[]
        {
            new BotPickMarketOptionViewModel("TotalCorners", "Córners totales"),
            new BotPickMarketOptionViewModel("HomeTeamCorners", "Córners local"),
            new BotPickMarketOptionViewModel("AwayTeamCorners", "Córners visita")
        });

    private static readonly BotPickMarketPageViewModel GoalsMarket = new(
        "goals",
        "Bot Picks · Goles",
        "Robot automático de goles",
        "Picks de goles producidos por el modelo Goals con ROI, yield, calibración y rendimiento por liga.",
        "goles",
        new[]
        {
            new BotPickMarketOptionViewModel("HomeTeamGoals", "Goles local"),
            new BotPickMarketOptionViewModel("AwayTeamGoals", "Goles visita"),
            new BotPickMarketOptionViewModel("TotalGoals", "Goles totales")
        });

    private static readonly BotPickMarketPageViewModel ShotsOnGoalMarket = new(
        "sog",
        "Bot Picks · Tiros al arco",
        "Robot automático SOG",
        "Picks de tiros al arco con seguimiento de edge, valor esperado, resultados y segmentos estadísticos.",
        "tiros al arco",
        new[]
        {
            new BotPickMarketOptionViewModel("HomeTeamShotsOnGoal", "Tiros al arco local"),
            new BotPickMarketOptionViewModel("AwayTeamShotsOnGoal", "Tiros al arco visita"),
            new BotPickMarketOptionViewModel("TotalShotsOnGoal", "Tiros al arco totales")
        });

    private static readonly BotPickMarketPageViewModel ShotsMarket = new(
        "shots",
        "Bot Picks · Tiros",
        "Robot automático de tiros totales",
        "Picks de tiros normales —incluye todos los remates, no solo los tiros al arco— generados por Modelos 2026.",
        "tiros",
        new[]
        {
            new BotPickMarketOptionViewModel("HomeTeamShots", "Tiros local"),
            new BotPickMarketOptionViewModel("AwayTeamShots", "Tiros visita"),
            new BotPickMarketOptionViewModel("TotalShots", "Tiros totales")
        });

}
