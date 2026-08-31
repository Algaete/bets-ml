using CornersPrediction.Application.AutomatedCorners;

var now = DateTime.UtcNow;
var rows = new List<AutomatedCornerSelectionDto>();
for (var index = 0; index < 40; index++)
    rows.Add(Pick(index + 1, "C2026", "HomeTeamCorners", index < 15, 0.70m, 0.55m));
for (var index = 0; index < 120; index++)
    rows.Add(Pick(1000 + index, "D2026", "AwayTeamGoals", index < 70, 0.58m, 0.55m));
rows.Add(Pick(9999, "D2026", "AwayTeamGoals", true, 0.58m, 0.55m, fixtureId: 1000));

var service = new AutomatedBotPerformanceService(new FakeRepository(rows));
var scorecards = await service.GetScorecardsAsync(CancellationToken.None);
var red = Find("C2026", "HomeTeamCorners", 30);
var green = Find("D2026", "AwayTeamGoals", 30);
var greenSide = FindSideBookmakerVersion(
    "D2026",
    "AwayTeamGoals",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV1.0-D2026",
    30);

Equal("Red", red.TrafficLight, "losing calibrated segment must be red");
if (!red.ProductionBlocked) throw new InvalidOperationException("Red segment did not block production.");
Equal("Green", green.TrafficLight, "stable positive segment must be green");
if (green.ProductionBlocked) throw new InvalidOperationException("Green segment blocked production.");
if (green.PredictiveResolved != 121 || green.PredictiveFixtures != 120)
    throw new InvalidOperationException("Scorecard did not separate correlated picks from independent fixtures.");
if (!scorecards.Any(row => row.WindowDays == 7)
    || !scorecards.Any(row => row.WindowDays == 30)
    || !scorecards.Any(row => row.WindowDays == 90))
    throw new InvalidOperationException("Expected 7/30/90 scorecard windows.");

Console.WriteLine("PASS server scorecards classify red and green segments");
Console.WriteLine("PASS server scorecards expose 7/30/90 windows");

var freshOdds = now.AddMinutes(-10);
var redGoalsFamily = new AutomatedBotPerformanceScorecard
{
    WindowDays = 30,
    Dimension = "BotFamily",
    BotKey = "D2026",
    MarketFamily = "GOALS",
    PredictiveResolved = 160,
    PredictiveFixtures = 160,
    TrafficLight = "Red",
    ProductionBlocked = true
};
var productionCards = new[] { greenSide, redGoalsFamily };
var eligible = AutomatedBotProductionEligibilityPolicy.Evaluate(
    productionCards, "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (!eligible.CanPublish) throw new InvalidOperationException($"Green half-line was blocked: {eligible.Reason}");
Equal("Green", eligible.Tier ?? string.Empty, "Green eligibility tier");
EqualDecimal(1m, eligible.MaxStakeUnits, "Green stake cap");

// The exact market/side/bookmaker/version segment is authoritative for GOALS.
// It must not be vetoed by a BotFamily aggregate polluted by paused TotalGoals.
var controlledTrialCard = greenSide with
{
    PredictiveResolved = 45,
    PredictiveFixtures = 45,
    Yield = 0.08m,
    CalibrationGap = 0.02d,
    DeltaBrier = -0.01d,
    TrafficLight = "Amber",
    ProductionBlocked = false
};
var controlledTrialCards = new[] { controlledTrialCard, redGoalsFamily };
var controlledTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (!controlledTrial.CanPublish)
    throw new InvalidOperationException($"Healthy GOALS controlled trial was blocked: {controlledTrial.Reason}");
Equal("ControlledTrial", controlledTrial.Tier ?? string.Empty, "controlled-trial eligibility tier");
EqualDecimal(0.5m, controlledTrial.MaxStakeUnits, "controlled-trial stake cap");

var homeGoalsTrialCard = controlledTrialCard with { MarketType = "HomeTeamGoals" };
var homeGoalsTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [homeGoalsTrialCard], "D2026", "GOALS", "HomeTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (!homeGoalsTrial.CanPublish || homeGoalsTrial.Tier != "ControlledTrial")
    throw new InvalidOperationException("Healthy HomeTeamGoals did not enter the controlled trial.");

var unhealthyTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { Yield = 0m }], "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (unhealthyTrial.CanPublish) throw new InvalidOperationException("GOALS without positive yield entered the controlled trial.");
var uncalibratedTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { CalibrationGap = 0.051d }], "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (uncalibratedTrial.CanPublish) throw new InvalidOperationException("GOALS outside the calibration limit entered the controlled trial.");
var worseThanMarketTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { DeltaBrier = 0.001d }], "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (worseThanMarketTrial.CanPublish) throw new InvalidOperationException("GOALS with positive delta Brier entered the controlled trial.");
var undersampledTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { PredictiveFixtures = 29 }], "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (undersampledTrial.CanPublish) throw new InvalidOperationException("GOALS with fewer than 30 independent fixtures entered the controlled trial.");

var quarter = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.25m, freshOdds, now);
if (quarter.CanPublish) throw new InvalidOperationException("Quarter line reached binary-EV production.");

var stale = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, now.AddHours(-3), now);
if (stale.CanPublish) throw new InvalidOperationException("Stale odds reached the controlled trial.");

var mutableOdds = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "D2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV1.0-D2026",
    1.5m,
    freshOdds,
    now,
    immutableOddsSnapshotAvailable: false);
if (mutableOdds.CanPublish) throw new InvalidOperationException("Mutable odds reached the controlled trial.");

var unprovenBookmaker = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "D2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Betano",
    "AutomatedCornersBotV1.0-D2026",
    1.5m,
    freshOdds,
    now);
if (unprovenBookmaker.CanPublish) throw new InvalidOperationException("A bookmaker without its own exact scorecard reached a GOALS trial.");

var unprovenSide = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "D2026",
    "GOALS",
    "AwayTeamGoals",
    "Under",
    "Pinnacle",
    "AutomatedCornersBotV1.0-D2026",
    1.5m,
    freshOdds,
    now);
if (unprovenSide.CanPublish) throw new InvalidOperationException("A side without its own exact scorecard reached a GOALS trial.");

var unprovenVersion = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "D2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV2.0-D2026",
    1.5m,
    freshOdds,
    now);
if (unprovenVersion.CanPublish) throw new InvalidOperationException("A new bot version inherited an older GOALS trial scorecard.");

var totalGoalsTrialCard = controlledTrialCard with { MarketType = "TotalGoals" };
var totalGoalsTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [totalGoalsTrialCard], "D2026", "GOALS", "TotalGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 2.5m, freshOdds, now);
if (totalGoalsTrial.CanPublish) throw new InvalidOperationException("TotalGoals entered a controlled trial.");

var damagedMarket = AutomatedBotProductionEligibilityPolicy.Evaluate(
    productionCards, "D2026", "CORNERS", "HomeTeamCorners", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 4.5m, freshOdds, now);
if (damagedMarket.CanPublish) throw new InvalidOperationException("HomeTeamCorners reached production.");

var cornersExactGreen = greenSide with
{
    MarketFamily = "CORNERS",
    MarketType = "AwayTeamCorners"
};
var redCornersFamily = redGoalsFamily with { MarketFamily = "CORNERS" };
var cornersFamilyVeto = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [cornersExactGreen, redCornersFamily], "D2026", "CORNERS", "AwayTeamCorners", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 4.5m, freshOdds, now);
if (cornersFamilyVeto.CanPublish) throw new InvalidOperationException("A Red non-GOALS family did not veto production.");

Console.WriteLine("PASS Green eligibility exposes a 1u authoritative cap");
Console.WriteLine("PASS healthy exact GOALS segments enter a 0.5u controlled trial");
Console.WriteLine("PASS controlled trial is fail-closed by side/bookmaker/version, freshness, snapshot and market health");
Console.WriteLine("PASS TotalGoals remains paused and non-GOALS retains its family veto");

AutomatedBotPerformanceScorecard Find(string bot, string market, int window) => scorecards.Single(row =>
    row.WindowDays == window
    && row.Dimension == "BotMarketType"
    && row.BotKey == bot
    && row.MarketType == market);

AutomatedBotPerformanceScorecard FindSideBookmakerVersion(
    string bot,
    string market,
    string side,
    string bookmaker,
    string automationVersion,
    int window) => scorecards.Single(row =>
    row.WindowDays == window
    && row.Dimension == "BotMarketSideBookmakerVersion"
    && row.BotKey == bot
    && row.MarketType == market
    && row.SelectedSide == side
    && row.Bookmaker == bookmaker
    && row.AutomationVersion == automationVersion);

AutomatedCornerSelectionDto Pick(
    long id,
    string bot,
    string market,
    bool won,
    decimal model,
    decimal marketProbability,
    long? fixtureId = null) => new()
{
    AutomatedCornerBetSelectionId = id,
    ApiFootballFixtureId = fixtureId ?? id,
    BotKey = bot,
    AutomationVersion = $"AutomatedCornersBotV1.0-{bot}",
    Source = "Pinnacle",
    MatchDate = now.AddDays(-1),
    MarketType = market,
    SelectedSide = "Over",
    LineValue = 1.5m,
    Status = won ? "Won" : "Lost",
    Stake = 1m,
    Odds = 1.90m,
    ProfitLoss = won ? 0.90m : -1m,
    ModelProbability = model,
    ImpliedProbability = marketProbability,
    ProbabilityEdge = model - marketProbability
};

static void Equal(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
}

static void EqualDecimal(decimal expected, decimal actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
}

sealed class FakeRepository(IReadOnlyList<AutomatedCornerSelectionDto> rows)
    : IAutomatedCornerSelectionsRepository
{
    public Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(
        AutomatedCornerSelectionsFilterRequest filters,
        CancellationToken cancellationToken) => Task.FromResult(rows);

    public Task<AutomatedCornerSelectionDto> UpdateStatusAsync(long id, UpdateAutomatedCornerSelectionStatusRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<AutomatedCornerSelectionDto> ResolveAsync(long id, int actualValue, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<AutomatedCornerSelectionDto> LinkMatchAsync(long id, long matchHistoryId, long apiFootballFixtureId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
