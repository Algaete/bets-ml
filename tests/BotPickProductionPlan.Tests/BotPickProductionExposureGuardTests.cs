using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;

internal static class BotPickProductionExposureGuardTests
{
    public static void RunAll()
    {
        CapsOnlyCurrentGoalsPlansAtTwoUnitsPerDay();
        RemovesCrossBotFixtureDuplicates();
        CanonicalOverlayMakesServerFiltersPlanNeutral();
        BuildsOnlyTheBoundedPendingUniverseWhenNeeded();
    }

    private static void CapsOnlyCurrentGoalsPlansAtTwoUnitsPerDay()
    {
        var day = new DateTime(2026, 9, 7, 12, 0, 0);
        var selections = new[]
        {
            Pick(1, "C2026", 101, day.AddHours(1), 0.50m, 0.30m),
            Pick(2, "F2026", 102, day.AddHours(2), 0.50m, 0.25m),
            Pick(3, "C2026", 103, day.AddHours(3), 0.50m, 0.20m),
            Pick(4, "F2026", 104, day.AddHours(4), 0.50m, 0.15m),
            Pick(5, "C2026", 105, day.AddHours(5), 0.50m, 0.10m),
            Pick(6, "F2026", 106, day.AddDays(1), 0.50m, 0.05m),
            Pick(7, "A", 107, day.AddHours(6), 1m, 0.01m, historical: true)
        };

        BotPickProductionExposureGuard.Apply(selections.Reverse().ToArray(), "goals");

        var currentFirstDay = selections
            .Where(selection => selection.AutomatedCornerBetSelectionId <= 5)
            .Where(selection => selection.ProductionPlan!.IsProductive)
            .ToArray();
        Equal(4, currentFirstDay.Length);
        Equal(2m, currentFirstDay.Sum(selection => selection.ProductionPlan!.StakeUnits));
        False(selections[4].ProductionPlan!.IsProductive);
        Contains("límite global GOALS", selections[4].ProductionPlan!.Reason);
        True(selections[5].ProductionPlan!.IsProductive);
        True(selections[6].ProductionPlan!.IsProductive);
        True(selections[6].ProductionPlan!.IsHistoricalReconstruction);
    }

    private static void RemovesCrossBotFixtureDuplicates()
    {
        var match = new DateTime(2026, 9, 8, 18, 0, 0);
        var botC = Pick(10, "C2026", 9001, match, 0.50m, 0.20m);
        var botF = Pick(11, "F2026", 9001, match.AddMinutes(1), 0.50m, 0.18m);

        BotPickProductionExposureGuard.Apply([botF, botC], "GOALS");

        Equal(1, new[] { botC, botF }.Count(selection => selection.ProductionPlan!.IsProductive));
        True(botC.ProductionPlan!.IsProductive);
        False(botF.ProductionPlan!.IsProductive);
        Contains("no se duplica exposición", botF.ProductionPlan!.Reason);
    }

    private static void CanonicalOverlayMakesServerFiltersPlanNeutral()
    {
        var match = new DateTime(2026, 9, 9, 18, 0, 0);
        var canonicalWinner = Pick(20, "C2026", 9100, match, 0.50m, 0.20m);
        var canonicalDuplicate = Pick(21, "F2026", 9100, match, 0.50m, 0.19m);
        BotPickProductionExposureGuard.Apply([canonicalWinner, canonicalDuplicate], "GOALS");

        // Simulates a bookmaker/market UI filter returning only Bot F. Its
        // local calculation is deliberately wrong; canonical overlay must not
        // promote it just because Bot C is hidden from the response.
        var filteredDuplicate = Pick(21, "F2026", 9100, match, 0.50m, 0.19m);
        BotPickProductionExposureGuard.OverlayCurrentPlans(
            [filteredDuplicate],
            [canonicalWinner, canonicalDuplicate]);

        False(filteredDuplicate.ProductionPlan!.IsProductive);
        Contains("no se duplica exposición", filteredDuplicate.ProductionPlan!.Reason);
    }

    private static void BuildsOnlyTheBoundedPendingUniverseWhenNeeded()
    {
        var filtered = new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1),
            DateTo = new DateTime(2026, 9, 30),
            Bookmaker = "Pinnacle",
            MarketType = "AwayTeamGoals",
            League = "England - Premier League"
        };

        True(BotPickProductionExposureGuard.RequiresCanonicalPendingUniverse(filtered, "goals"));
        False(BotPickProductionExposureGuard.RequiresCanonicalPendingUniverse(filtered, "corners"));

        var canonical = BotPickProductionExposureGuard.CreateCanonicalPendingFilters(filtered);
        Equal(filtered.DateFrom, canonical.DateFrom);
        Equal(filtered.DateTo, canonical.DateTo);
        True(canonical.OnlyPending);
        Empty(canonical.Bookmaker);
        Empty(canonical.MarketType);
        Empty(canonical.League);

        filtered.Status = "Won";
        False(BotPickProductionExposureGuard.RequiresCanonicalPendingUniverse(filtered, "goals"));
    }

    private static BotPickSelectionViewModel Pick(
        long id,
        string botKey,
        long fixtureId,
        DateTime matchDate,
        decimal stakeUnits,
        decimal expectedValue,
        bool historical = false) => new()
    {
        AutomatedCornerBetSelectionId = id,
        ApiFootballFixtureId = fixtureId,
        BotKey = botKey,
        AutomationVersion = $"AutomatedCornersBotV1.0-{botKey}",
        MatchDate = matchDate,
        MatchDay = matchDate.Date,
        HomeTeam = $"Home {fixtureId}",
        AwayTeam = $"Away {fixtureId}",
        StandardizedHomeTeam = $"Home {fixtureId}",
        StandardizedAwayTeam = $"Away {fixtureId}",
        MarketType = "AwayTeamGoals",
        SelectedSide = "Over",
        LineValue = 1.5m,
        Odds = 1.90m,
        ProbabilityEdge = expectedValue / 2m,
        ExpectedValue = expectedValue,
        SelectionScore = expectedValue,
        Status = historical ? "Won" : "Pending",
        ProductionPlan = new BotPickProductionPlanViewModel(
            stakeUnits == 1m ? "stake-1" : "stake-half",
            stakeUnits,
            historical ? "Histórico reconstruido 1u" : "Prueba controlada 0.5u",
            "test",
            "bot-production-secondary",
            true,
            historical ? "GOALS-HISTORICAL-RECONSTRUCTION-V1" : "PRODUCTIVE-GATE-TEST",
            historical)
    };

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true, got false.");
    }

    private static void False(bool value) => True(!value);

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    private static void Empty(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Expected an empty value, got '{value}'.");
    }
}
