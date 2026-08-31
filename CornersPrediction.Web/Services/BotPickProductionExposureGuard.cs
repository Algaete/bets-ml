using System.Text.RegularExpressions;
using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Services;

/// <summary>
/// Applies portfolio-level safety rules after the per-pick productive gate has
/// selected its canonical recommendation. Historical reconstructions are
/// deliberately outside this guard: they describe an old simulation and must
/// never consume or be rewritten by today's exposure budget.
/// </summary>
public static class BotPickProductionExposureGuard
{
    public const decimal MaximumGoalsUnitsPerDay = 2m;

    public static void Apply(
        IReadOnlyCollection<BotPickSelectionViewModel> selections,
        string marketFamily)
    {
        ArgumentNullException.ThrowIfNull(selections);

        if (!marketFamily.Trim().Equals("GOALS", StringComparison.OrdinalIgnoreCase))
            return;

        var currentProductive = selections
            .Where(selection => selection.ProductionPlan is
            {
                IsProductive: true,
                IsHistoricalReconstruction: false,
                StakeUnits: > 0m
            })
            .ToArray();

        // The planner already emits one row per fixture. This second boundary
        // is intentional defence-in-depth for filtered views and source rows
        // whose timestamps or team spellings differ between Bot C and Bot F.
        var canonicalByFixture = new List<BotPickSelectionViewModel>();
        foreach (var fixture in currentProductive.GroupBy(FixtureKey, StringComparer.OrdinalIgnoreCase))
        {
            var ranked = Rank(fixture).ToArray();
            var winner = ranked[0];
            canonicalByFixture.Add(winner);

            foreach (var duplicate in ranked.Skip(1))
            {
                Suppress(
                    duplicate,
                    $"Monitoreo: el partido ya está cubierto por la selección #{winner.AutomatedCornerBetSelectionId}; no se duplica exposición entre bots.");
            }
        }

        foreach (var day in canonicalByFixture.GroupBy(ProductionDay))
        {
            var remainingUnits = MaximumGoalsUnitsPerDay;
            foreach (var selection in Rank(day))
            {
                var stakeUnits = selection.ProductionPlan!.StakeUnits;
                if (stakeUnits <= remainingUnits)
                {
                    remainingUnits -= stakeUnits;
                    continue;
                }

                Suppress(
                    selection,
                    $"Monitoreo: límite global GOALS de {MaximumGoalsUnitsPerDay:0.#}u para {day.Key:dd-MM-yyyy} alcanzado; se priorizan señales Green y luego EV, edge y score.");
            }
        }
    }

    /// <summary>
    /// A narrow server-side filter must not turn a losing Bot C/F duplicate or
    /// a pick beyond the daily limit into a productive recommendation merely
    /// because its canonical competitor is hidden from the page.
    /// </summary>
    public static bool RequiresCanonicalPendingUniverse(
        BotPickFiltersViewModel filters,
        string marketFamily)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (!marketFamily.Trim().Equals("GOALS", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(filters.Status)
            && !filters.Status.Trim().Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(filters.League)
            || !string.IsNullOrWhiteSpace(filters.Bookmaker)
            || !string.IsNullOrWhiteSpace(filters.MarketType);
    }

    public static BotPickFiltersViewModel CreateCanonicalPendingFilters(BotPickFiltersViewModel filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        return new BotPickFiltersViewModel
        {
            DateFrom = filters.DateFrom,
            DateTo = filters.DateTo,
            OnlyPending = true
        };
    }

    public static void OverlayCurrentPlans(
        IReadOnlyCollection<BotPickSelectionViewModel> displayedSelections,
        IReadOnlyCollection<BotPickSelectionViewModel> canonicalSelections)
    {
        ArgumentNullException.ThrowIfNull(displayedSelections);
        ArgumentNullException.ThrowIfNull(canonicalSelections);

        var canonicalPlans = canonicalSelections
            .Where(selection => selection.ProductionPlan is { IsHistoricalReconstruction: false })
            .GroupBy(selection => selection.AutomatedCornerBetSelectionId)
            .ToDictionary(group => group.Key, group => group.First().ProductionPlan!);

        foreach (var selection in displayedSelections)
        {
            if (canonicalPlans.TryGetValue(selection.AutomatedCornerBetSelectionId, out var plan))
                selection.ProductionPlan = plan;
        }
    }

    public static void FailClosedCurrentPlans(
        IReadOnlyCollection<BotPickSelectionViewModel> selections,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        foreach (var selection in selections.Where(selection =>
                     selection.ProductionPlan is { IsHistoricalReconstruction: false }))
        {
            Suppress(selection, reason);
        }
    }

    private static IOrderedEnumerable<BotPickSelectionViewModel> Rank(
        IEnumerable<BotPickSelectionViewModel> selections) => selections
        // In GOALS, 1u is only available to a Green cohort with >=100
        // independent fixtures; the 0.5u cohorts are controlled trials.
        .OrderByDescending(selection => selection.ProductionPlan!.StakeUnits >= 1m)
        .ThenByDescending(selection => selection.ExpectedValue ?? decimal.MinValue)
        .ThenByDescending(selection => selection.ProbabilityEdge ?? decimal.MinValue)
        .ThenByDescending(selection => selection.SelectionScore ?? decimal.MinValue)
        .ThenBy(selection => selection.MatchDate)
        .ThenBy(selection => selection.AutomatedCornerBetSelectionId);

    private static DateTime ProductionDay(BotPickSelectionViewModel selection) =>
        (selection.MatchDay ?? selection.MatchDate).Date;

    private static string FixtureKey(BotPickSelectionViewModel selection)
    {
        if (selection.ApiFootballFixtureId is > 0)
            return $"API:{selection.ApiFootballFixtureId.Value}";

        return string.Join('|',
            selection.MatchDate.ToString("yyyy-MM-ddTHH:mm"),
            Normalize(selection.StandardizedHomeTeam ?? selection.HomeTeam),
            Normalize(selection.StandardizedAwayTeam ?? selection.AwayTeam));
    }

    private static string Normalize(string? value) =>
        Regex.Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, @"\s+", " ");

    private static void Suppress(BotPickSelectionViewModel selection, string reason)
    {
        var previous = selection.ProductionPlan;
        selection.ProductionPlan = new BotPickProductionPlanViewModel(
            "monitor",
            0m,
            "Monitoreo",
            reason,
            "bot-production-monitor",
            false,
            previous?.PolicyVersion ?? string.Empty,
            false);
    }
}
