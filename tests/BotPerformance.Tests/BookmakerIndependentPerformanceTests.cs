using CornersPrediction.Application.AutomatedCorners;

internal static class BookmakerIndependentPerformanceTests
{
    public static async Task RunAsync()
    {
        var now = DateTime.UtcNow;
        const string version = "AutomatedCornersBotV1.0-F2026";
        var observations = Enumerable.Range(0, 31)
            .Select(index => Evidence(index + 1, index + 100, index < 18, now))
            .ToArray();
        var baselineCards = await Cards(observations);
        var baseline = ProductionCard(baselineCards, version);
        Require(baseline.Total == 31 && baseline.PredictiveFixtures == 31,
            "Baseline must contain 31 independent fixtures.");

        // The same decision at a second house, a retry at a later price, and a
        // different later line must not change the first observed decision.
        var duplicateHouse = observations[0] with
        {
            EvidenceId = 1_001,
            Bookmaker = "Betano",
            Odds = 4.90m,
            ProfitLoss = 3.90m,
            MarketProbability = 0.20m,
            OutcomeAvailableAtUtc = now.AddHours(-2)
        };
        var laterLine = observations[0] with
        {
            EvidenceId = 1_002,
            Bookmaker = "Another house",
            LineValue = 2.5m,
            Result = "Lost",
            ProfitLoss = -1m,
            ModelProbability = 0.10m,
            MarketProbability = 0.15m,
            DecisionAtUtc = observations[0].DecisionAtUtc.AddHours(1),
            OutcomeAvailableAtUtc = now.AddHours(-1)
        };
        var differentVersion = observations[0] with
        {
            EvidenceId = 2_001,
            AutomationVersion = "AutomatedCornersBotV2.0-F2026",
            Result = "Lost",
            ProfitLoss = -1m
        };
        var rejected = observations[0] with { EvidenceId = 3_001, ApiFootballFixtureId = 3_001, ModelDecision = "Rejected" };
        var futureOutcome = observations[0] with { EvidenceId = 3_002, ApiFootballFixtureId = 3_002, OutcomeAvailableAtUtc = now.AddDays(1) };
        var afterKickoff = observations[0] with { EvidenceId = 3_003, ApiFootballFixtureId = 3_003, DecisionAtUtc = now.AddDays(-1) };
        var quarterLine = observations[0] with { EvidenceId = 3_004, ApiFootballFixtureId = 3_004, LineValue = 1.25m };
        var futureFixture = observations[0] with { EvidenceId = 3_005, ApiFootballFixtureId = 3_005, FixtureDateUtc = now.AddDays(1) };
        var modifiedObservations = observations.Concat([
            duplicateHouse, laterLine, differentVersion, rejected,
            futureOutcome, afterKickoff, quarterLine, futureFixture]).ToArray();
        var combinedCards = await Cards(modifiedObservations);
        var combined = ProductionCard(combinedCards, version);
        SameIndependentEvidence(baseline, combined);
        SameIndependentEvidence(combined, ProductionCard(await Cards(modifiedObservations.Reverse().ToArray()), version));
        Require(combined.Bookmaker is null, "Production evidence must not retain a house gate.");
        Require(combinedCards.Any(row => row.WindowDays == 30
            && row.Dimension == "BotMarketSideBookmakerVersion" && row.Bookmaker == "Betano"),
            "Bookmaker diagnostics were removed.");
        var newVersion = ProductionCard(combinedCards, differentVersion.AutomationVersion);
        Require(newVersion.PredictiveFixtures == 1 && newVersion.ProfitLoss == -1m,
            "Different model versions mixed their sample or financial results.");
        Require(combined.SettledStake == 31m && combined.ProfitLoss == 3.2m,
            "Prices, line outcomes or duplicate houses changed the chosen observations' actual P/L.");

        AutomatedBotProductionEligibility Evaluate(string bookmaker,
            DateTime? oddsTime = null,
            bool snapshot = true,
            string candidateVersion = version) => AutomatedBotProductionEligibilityPolicy.Evaluate(
                combinedCards, "F2026", "GOALS", "AwayTeamGoals", "Over", bookmaker,
                candidateVersion, 1.5m, oddsTime ?? now.AddMinutes(-5), now,
                immutableOddsSnapshotAvailable: snapshot);
        var pinnacle = Evaluate("Pinnacle");
        Require(pinnacle.CanPublish && pinnacle.Tier == "ControlledTrial" && pinnacle.MaxStakeUnits == 0.5m,
            "The healthy independent cohort no longer passes the controlled trial.");
        foreach (var bookmaker in new[] { "Betano", "PINNACLE", "Other house", string.Empty })
        {
            var candidate = Evaluate(bookmaker);
            Require(candidate.CanPublish == pinnacle.CanPublish && candidate.Tier == pinnacle.Tier
                && candidate.MaxStakeUnits == pinnacle.MaxStakeUnits && candidate.Reason == pinnacle.Reason,
                $"A bookmaker name changed the decision: {bookmaker}.");
            Require(!Evaluate(bookmaker, now.AddHours(-3)).CanPublish,
                $"Stale odds passed after removing the house gate: {bookmaker}.");
            Require(!Evaluate(bookmaker, snapshot: false).CanPublish,
                $"Missing immutable odds snapshot passed: {bookmaker}.");
        }
        Require(!Evaluate("Betano", candidateVersion: differentVersion.AutomationVersion).CanPublish,
            "A new version inherited sufficient evidence from the previous version.");
        var diagnosticOnly = AutomatedBotProductionEligibilityPolicy.Evaluate(
            combinedCards.Where(row => row.Dimension != "BotMarketSideVersion").ToArray(),
            "F2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", version,
            1.5m, now.AddMinutes(-5), now);
        Require(!diagnosticOnly.CanPublish, "House scorecards were used as a fallback or summed for production.");

        // Published picks from another house must not reintroduce publication bias
        // into an independently covered scientific segment.
        var publishedElsewhere = new AutomatedCornerSelectionDto
        {
            AutomatedCornerBetSelectionId = 90_001,
            ApiFootballFixtureId = 90_001,
            BotKey = "F2026",
            AutomationVersion = version,
            MarketType = "AwayTeamGoals",
            SelectedSide = "Over",
            Source = "Published-only house",
            LineValue = 1.5m,
            MatchDate = now.AddDays(-2),
            Status = "Lost",
            Stake = 20m,
            ProfitLoss = -20m,
            ModelProbability = 0.9m,
            ImpliedProbability = 0.5m
        };
        var mixedCards = await new AutomatedBotPerformanceService(
            new FakeRepository([publishedElsewhere]),
            new FakeEvidenceRepository(observations)).GetScorecardsAsync(CancellationToken.None);
        SameIndependentEvidence(baseline, ProductionCard(mixedCards, version));
        Require(mixedCards.Any(row => row.WindowDays == 30
            && row.Dimension == "BotMarketSideBookmakerVersion" && row.Bookmaker == publishedElsewhere.Source),
            "Published-only house lost its diagnostic scorecard.");

        Console.WriteLine("PASS production ignores bookmaker names while retaining freshness, snapshot, version and stake limits");
        Console.WriteLine("PASS cross-house scorecards count each fixture once with its first decision's actual odds and P/L");
        Console.WriteLine("PASS later prices, retries, settlement order and input order cannot select more favorable evidence");
        Console.WriteLine("PASS bookmaker diagnostics remain available without overriding scientific production evidence");
    }

    private static Task<IReadOnlyList<AutomatedBotPerformanceScorecard>> Cards(
        IReadOnlyList<AutomatedBotPerformanceEvidence> observations) =>
        new AutomatedBotPerformanceService(new FakeRepository([]), new FakeEvidenceRepository(observations))
            .GetScorecardsAsync(CancellationToken.None);

    private static AutomatedBotPerformanceScorecard ProductionCard(
        IReadOnlyList<AutomatedBotPerformanceScorecard> cards,
        string version) => cards.Single(row => row.WindowDays == 30
            && row.Dimension == "BotMarketSideVersion"
            && row.BotKey == "F2026" && row.MarketType == "AwayTeamGoals"
            && row.SelectedSide == "Over" && row.AutomationVersion == version);

    private static void SameIndependentEvidence(
        AutomatedBotPerformanceScorecard expected,
        AutomatedBotPerformanceScorecard actual)
    {
        Require(expected.Total == actual.Total && expected.Resolved == actual.Resolved
            && expected.PredictiveResolved == actual.PredictiveResolved
            && expected.PredictiveFixtures == actual.PredictiveFixtures
            && expected.ScientificPredictiveResolved == actual.ScientificPredictiveResolved
            && expected.PublishedPredictiveResolved == actual.PublishedPredictiveResolved
            && expected.SettledStake == actual.SettledStake && expected.ProfitLoss == actual.ProfitLoss
            && expected.Yield == actual.Yield && Close(expected.ObservedWinRate, actual.ObservedWinRate)
            && Close(expected.AverageModelProbability, actual.AverageModelProbability)
            && Close(expected.AverageMarketProbability, actual.AverageMarketProbability)
            && Close(expected.Brier, actual.Brier) && Close(expected.MarketBrier, actual.MarketBrier)
            && Close(expected.CalibrationGap, actual.CalibrationGap)
            && Close(expected.DeltaBrier, actual.DeltaBrier),
            "Correlated or unsafe evidence changed the independent production scorecard.");
    }

    private static bool Close(double? expected, double? actual) =>
        expected.HasValue && actual.HasValue
            ? Math.Abs(expected.Value - actual.Value) < 1e-12d
            : expected == actual;

    private static AutomatedBotPerformanceEvidence Evidence(long id, long fixtureId, bool won, DateTime now) => new()
    {
        EvidenceId = id,
        EvidenceKey = $"evaluation:{id}",
        ApiFootballFixtureId = fixtureId,
        BotKey = "F2026",
        AutomationVersion = "AutomatedCornersBotV1.0-F2026",
        FixtureDateUtc = now.AddDays(-2),
        Bookmaker = "Pinnacle",
        MarketType = "AwayTeamGoals",
        SelectedSide = "Over",
        LineValue = 1.5m,
        Odds = 1.90m,
        StakeUnits = 1m,
        ModelDecision = "Approved",
        Result = won ? "Won" : "Lost",
        SettlementFactor = won ? 1m : -1m,
        ProfitLoss = won ? 0.90m : -1m,
        ModelProbability = 0.58m,
        MarketProbability = 0.55m,
        ProbabilityEdge = 0.03m,
        DecisionAtUtc = now.AddDays(-3),
        OutcomeAvailableAtUtc = now.AddDays(-1)
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
