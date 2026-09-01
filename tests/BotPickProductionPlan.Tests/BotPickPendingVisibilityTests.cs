using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;

internal static class BotPickPendingVisibilityTests
{
    public static void RunAll()
    {
        DefaultWindowCrossesMonthEndBySevenDays();
        DefaultWindowStillIncludesTheWholeCurrentMonth();
        ExplicitRangeIsNeverExpanded();
        PartialExplicitRangeKeepsPreviousCompletionRules();
        OlderPendingRowsRemainOperationallyVisible();
        OlderPendingMergeIsBoundedAndDistinct();
    }

    private static void OlderPendingRowsRemainOperationallyVisible()
    {
        var filters = new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1),
            DateTo = new DateTime(2026, 9, 30),
            League = "Chile - Primera Division",
            Bookmaker = "Pinnacle",
            MarketType = "HomeTeamCorners"
        };

        if (!BotPickVisibilityPolicy.ShouldLoadOlderPending(filters))
            throw new InvalidOperationException("An unfiltered Bot Picks page must load unresolved older picks.");

        var older = BotPickVisibilityPolicy.CreateOlderPendingFilters(filters);
        if (older.DateFrom.HasValue)
            throw new InvalidOperationException("The older-pending query must not have a lower date boundary.");
        Equal(new DateTime(2026, 8, 31), older.DateTo);
        if (!older.OnlyPending || older.Status != "Pending")
            throw new InvalidOperationException("The additional query must be limited to pending rows.");
        Equal("Chile - Primera Division", older.League);
        Equal("Pinnacle", older.Bookmaker);
        Equal("HomeTeamCorners", older.MarketType);

        filters.Status = "Won";
        if (BotPickVisibilityPolicy.ShouldLoadOlderPending(filters))
            throw new InvalidOperationException("A resolved-only historical filter must keep its exact range.");
    }

    private static void OlderPendingMergeIsBoundedAndDistinct()
    {
        var boundary = new DateTime(2026, 9, 1);
        var current = new[]
        {
            Pick(10, new DateTime(2026, 9, 2), "Pending"),
            Pick(11, new DateTime(2026, 9, 3), "Won")
        };
        var older = new[]
        {
            Pick(10, new DateTime(2026, 8, 20), "Pending"),
            Pick(12, new DateTime(2026, 8, 30), "Pending"),
            Pick(13, new DateTime(2026, 8, 29), "Won"),
            Pick(14, new DateTime(2026, 9, 1), "Pending")
        };

        var merged = BotPickVisibilityPolicy.MergeDistinct(current, older, boundary);

        Equal(
            [10L, 11L, 12L],
            merged.Select(selection => selection.AutomatedCornerBetSelectionId).Order().ToArray());
        Equal(
            new DateTime(2026, 9, 2),
            merged.Single(selection => selection.AutomatedCornerBetSelectionId == 10).MatchDate);
    }

    private static void DefaultWindowCrossesMonthEndBySevenDays()
    {
        var filters = new BotPickFiltersViewModel();

        BotPickDefaultDateRange.Apply(filters, new DateTime(2026, 8, 31));

        Equal(new DateTime(2026, 8, 1), filters.DateFrom);
        Equal(new DateTime(2026, 9, 7), filters.DateTo);
    }

    private static void DefaultWindowStillIncludesTheWholeCurrentMonth()
    {
        var filters = new BotPickFiltersViewModel();

        BotPickDefaultDateRange.Apply(filters, new DateTime(2026, 8, 10));

        Equal(new DateTime(2026, 8, 1), filters.DateFrom);
        Equal(new DateTime(2026, 8, 31), filters.DateTo);
    }

    private static void ExplicitRangeIsNeverExpanded()
    {
        var filters = new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 7, 10),
            DateTo = new DateTime(2026, 7, 12)
        };

        BotPickDefaultDateRange.Apply(filters, new DateTime(2026, 8, 31));

        Equal(new DateTime(2026, 7, 10), filters.DateFrom);
        Equal(new DateTime(2026, 7, 12), filters.DateTo);
    }

    private static void PartialExplicitRangeKeepsPreviousCompletionRules()
    {
        var fromOnly = new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 7, 10)
        };
        var toOnly = new BotPickFiltersViewModel
        {
            DateTo = new DateTime(2026, 7, 12)
        };

        BotPickDefaultDateRange.Apply(fromOnly, new DateTime(2026, 8, 31));
        BotPickDefaultDateRange.Apply(toOnly, new DateTime(2026, 8, 31));

        Equal(new DateTime(2026, 7, 10), fromOnly.DateFrom);
        Equal(new DateTime(2026, 7, 31), fromOnly.DateTo);
        Equal(new DateTime(2026, 7, 1), toOnly.DateFrom);
        Equal(new DateTime(2026, 7, 12), toOnly.DateTo);
    }

    private static void Equal(DateTime expected, DateTime? actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected:yyyy-MM-dd}, got {actual:yyyy-MM-dd}.");
        }
    }

    private static void Equal(string expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Equal(long[] expected, long[] actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    private static BotPickSelectionViewModel Pick(long id, DateTime matchDate, string status) => new()
    {
        AutomatedCornerBetSelectionId = id,
        MatchDate = matchDate,
        MarketType = "HomeTeamCorners",
        Status = status
    };
}
