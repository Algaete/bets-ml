using CornersPrediction.Application.AutomatedCorners;

internal static class ProductionYieldPolicyTests
{
    public static void Run(AutomatedBotPerformanceScorecard seed, DateTime now)
    {
        var markets = new (string Family, string Market)[]
        {
            ("GOALS", "HomeTeamGoals"), ("GOALS", "AwayTeamGoals"), ("GOALS", "TotalGoals"),
            ("CORNERS", "HomeTeamCorners"), ("CORNERS", "AwayTeamCorners"), ("CORNERS", "TotalCorners"),
            ("SHOTS", "TotalShots"), ("SOG", "TotalShotsOnGoal")
        };
        foreach (var bot in new[] { "A", "C2026", "D2026", "E2026", "F2026" })
        foreach (var (family, market) in markets)
        foreach (var side in new[] { "Over", "Under" })
        foreach (var yield in new decimal?[] { null, -0.1m, 0m, 0.029999m, 0.03m, 0.030001m, 0.12m })
        {
            // Deliberately poor diagnostics, small sample and a red family must not
            // reintroduce the retired gates when the exact segment meets 3%.
            var card = seed with
            {
                BotKey = bot, MarketFamily = family, MarketType = market, SelectedSide = side,
                AutomationVersion = $"Current-{bot}", Yield = yield,
                PredictiveFixtures = 1, PredictiveResolved = 1,
                TrafficLight = "Red", ProductionBlocked = true,
                CalibrationGap = 0.2, DeltaBrier = 0.1
            };
            var aggregate = card with { Dimension = "BotFamily", Yield = -1m };
            var actual = Evaluate([card, aggregate], card);
            Require(actual.CanPublish == (yield >= 0.03m), $"{bot}/{market}/{side}/{yield}: {actual.Reason}");
            if (actual.CanPublish)
            {
                var capped = (bot is "C2026" or "F2026") && market == "AwayTeamGoals" && side == "Over";
                Require(actual.MaxStakeUnits == (capped ? 0.5m : 1m), "Stake cap changed.");
                var otherBookmaker = Evaluate([card, aggregate], card, bookmaker: "Another bookmaker");
                Require(otherBookmaker.CanPublish && otherBookmaker.MaxStakeUnits == actual.MaxStakeUnits,
                    "Bookmaker changed eligibility or stake.");
            }
        }
        Console.WriteLine("PASS yield threshold is inclusive at exactly 3%, across bots, sides and all market families");
        Console.WriteLine("PASS sample size, Brier, calibration, red diagnostics and former market pauses do not veto yield");
        Console.WriteLine("PASS missing, negative and sub-3% yield stay blocked; bookmaker independence and stake caps remain");

        var healthy = seed with { Yield = 0.03m, CalibrationGap = null, DeltaBrier = null };
        Require(Evaluate([healthy], healthy).CanPublish, "Missing optional diagnostics blocked known yield.");
        foreach (var otherCard in new[]
        {
            healthy with { WindowDays = 7 }, healthy with { WindowDays = 90 },
            healthy with { Dimension = "BotMarketSideBookmakerVersion" },
            healthy with { BotKey = "AnotherBot" }, healthy with { MarketType = "OtherMarket" },
            healthy with { SelectedSide = "Under" }, healthy with { AutomationVersion = "PreviousVersion" }
        })
            Require(!Evaluate([otherCard], healthy).CanPublish, "Unrelated evidence qualified a segment.");
        Require(!Evaluate([], healthy).CanPublish, "Missing scorecard qualified a segment.");
        Require(!Evaluate([healthy], healthy, line: 1.25m).CanPublish, "Quarter line passed binary EV.");
        Require(!Evaluate([healthy], healthy, oddsTime: now.AddHours(-3)).CanPublish, "Stale quote passed.");
        Require(!Evaluate([healthy], healthy, oddsTime: now.AddMinutes(1)).CanPublish, "Future quote passed.");
        Require(!Evaluate([healthy], healthy, immutable: false).CanPublish, "Missing quote snapshot passed.");
        Console.WriteLine("PASS exact 30-day version/market/side evidence and quote integrity remain required");

        AutomatedBotProductionEligibility Evaluate(
            IReadOnlyCollection<AutomatedBotPerformanceScorecard> cards,
            AutomatedBotPerformanceScorecard candidate,
            decimal line = 1.5m, DateTime? oddsTime = null, bool immutable = true,
            string bookmaker = "Pinnacle") => AutomatedBotProductionEligibilityPolicy.Evaluate(
                cards, candidate.BotKey!, candidate.MarketFamily!, candidate.MarketType!, candidate.SelectedSide!,
                bookmaker, candidate.AutomationVersion!, line, oddsTime ?? now.AddMinutes(-10), now,
                immutableOddsSnapshotAvailable: immutable);
    }

    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
