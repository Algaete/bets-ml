using CornersPrediction.Web.Models.BotPicks;

namespace CornersPrediction.Web.Services;

/// <summary>
/// Keeps retired-bot history out of the active Bot Picks surface while retaining
/// unresolved rows that an operator still needs to reconcile or settle.
/// </summary>
public static class BotPickVisibilityPolicy
{
    private const string RetiredBotKey = "B";
    private const string SafetyPolicyVersion = "RETIRED-BOT-PENDING-MONITOR-V1";

    public static IReadOnlyList<BotPickSelectionViewModel> Filter(
        IReadOnlyList<BotPickSelectionViewModel> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        return selections
            .Where(selection => !IsRetiredBot(selection) || IsPending(selection))
            .ToArray();
    }

    /// <summary>
    /// Pending rows are operational work, so the default/current date window
    /// must not make an older unresolved pick disappear. Resolved-only filters
    /// keep their exact historical range.
    /// </summary>
    public static bool ShouldLoadOlderPending(BotPickFiltersViewModel filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        return filters.DateFrom.HasValue
            && (filters.OnlyPending
                || string.IsNullOrWhiteSpace(filters.Status)
                || filters.Status.Trim().Equals("Pending", StringComparison.OrdinalIgnoreCase));
    }

    public static BotPickFiltersViewModel CreateOlderPendingFilters(BotPickFiltersViewModel filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        if (!filters.DateFrom.HasValue)
            throw new ArgumentException("A lower date boundary is required.", nameof(filters));

        return new BotPickFiltersViewModel
        {
            DateTo = filters.DateFrom.Value.Date.AddDays(-1),
            Status = "Pending",
            League = filters.League,
            Bookmaker = filters.Bookmaker,
            MarketType = filters.MarketType,
            OnlyPending = true
        };
    }

    public static IReadOnlyList<BotPickSelectionViewModel> MergeDistinct(
        IEnumerable<BotPickSelectionViewModel> current,
        IEnumerable<BotPickSelectionViewModel> olderPending,
        DateTime olderThan) =>
        current
            // The API query already requests this exact subset. Revalidate at
            // the Web boundary so a stale stored procedure or proxy cannot
            // leak resolved/out-of-window history into the operational list.
            .Concat(olderPending.Where(selection =>
                IsPending(selection)
                && selection.MatchDate.Date < olderThan.Date))
            .GroupBy(selection => selection.AutomatedCornerBetSelectionId)
            .Select(group => group.First())
            .OrderByDescending(selection => selection.MatchDate)
            .ThenByDescending(selection => selection.AutomatedCornerBetSelectionId)
            .ToArray();

    public static void EnforceRetiredPendingMonitoring(
        IEnumerable<BotPickSelectionViewModel> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        foreach (var selection in selections.Where(IsRetiredBot).Where(IsPending))
        {
            selection.ProductionPlan = new BotPickProductionPlanViewModel(
                "monitor",
                0m,
                "Pendiente heredado · 0u",
                "Bot B está retirado: esta fila sólo se conserva para revisión o liquidación y nunca habilita una apuesta nueva.",
                "bot-production-monitor",
                false,
                SafetyPolicyVersion);
        }
    }

    private static bool IsPending(BotPickSelectionViewModel selection) =>
        string.Equals(selection.Status, "Pending", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetiredBot(BotPickSelectionViewModel selection)
    {
        if (string.Equals(selection.BotKey?.Trim(), RetiredBotKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (selection.AutomationVersion.Trim().EndsWith("-B", StringComparison.OrdinalIgnoreCase))
            return true;

        return TryResolveDecisionBotKey(selection.DecisionReason, out var botKey)
            && string.Equals(botKey, RetiredBotKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveDecisionBotKey(string? decisionReason, out string? botKey)
    {
        botKey = null;
        if (string.IsNullOrWhiteSpace(decisionReason))
            return false;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(decisionReason);
            if (!document.RootElement.TryGetProperty("botProfile", out var profile))
                return false;

            botKey = profile.GetString()?.Trim();
            return !string.IsNullOrWhiteSpace(botKey);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
