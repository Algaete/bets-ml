using CornersPrediction.Application.AutomatedCorners;
using Microsoft.SqlServer.TransactSql.ScriptDom;

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
var greenSide = FindSideVersion(
    "D2026",
    "AwayTeamGoals",
    "Over",
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

// The exact market/side/version segment is authoritative for GOALS.
// It must not be vetoed by a BotFamily aggregate polluted by paused TotalGoals.
var controlledTrialCard = greenSide with
{
    BotKey = "C2026",
    AutomationVersion = "AutomatedCornersBotV1.0-C2026",
    PredictiveResolved = 45,
    PredictiveFixtures = 45,
    Yield = 0.08m,
    CalibrationGap = 0.02d,
    DeltaBrier = -0.01d,
    TrafficLight = "Amber",
    ProductionBlocked = false
};
var controlledTrialCards = new[]
{
    controlledTrialCard,
    redGoalsFamily with { BotKey = "C2026" }
};
var controlledTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (!controlledTrial.CanPublish)
    throw new InvalidOperationException($"Healthy GOALS controlled trial was blocked: {controlledTrial.Reason}");
Equal("ControlledTrial", controlledTrial.Tier ?? string.Empty, "controlled-trial eligibility tier");
EqualDecimal(0.5m, controlledTrial.MaxStakeUnits, "controlled-trial stake cap");

var homeGoalsTrialCard = controlledTrialCard with { MarketType = "HomeTeamGoals" };
var homeGoalsTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [homeGoalsTrialCard], "C2026", "GOALS", "HomeTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (homeGoalsTrial.CanPublish)
    throw new InvalidOperationException("HomeTeamGoals entered the frozen AwayTeamGoals trial.");
if (!homeGoalsTrial.Reason.Contains("45/100", StringComparison.Ordinal))
    throw new InvalidOperationException("HomeTeamGoals did not explain its actual 100-fixture requirement.");

var homeGoalsGreen = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [greenSide with { MarketType = "HomeTeamGoals" }, redGoalsFamily],
    "D2026", "GOALS", "HomeTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (!homeGoalsGreen.CanPublish || homeGoalsGreen.Tier != "Green")
    throw new InvalidOperationException($"HomeTeamGoals with its own Green evidence was incorrectly limited to away goals: {homeGoalsGreen.Reason}");

var insufficientShots = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { MarketFamily = "SHOTS", MarketType = "TotalShots" }],
    "C2026", "SHOTS", "TotalShots", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 20.5m, freshOdds, now);
if (insufficientShots.CanPublish || !insufficientShots.Reason.Contains("45/100", StringComparison.Ordinal))
    throw new InvalidOperationException("SHOTS did not report the general Green sample requirement.");

var challengerTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { BotKey = "D2026", AutomationVersion = "AutomatedCornersBotV1.0-D2026" }],
    "D2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-D2026", 1.5m, freshOdds, now);
if (challengerTrial.CanPublish)
    throw new InvalidOperationException("A D/E challenger entered the C2026/F2026 controlled trial.");

var botFTrialCard = controlledTrialCard with
{
    BotKey = "F2026",
    AutomationVersion = "AutomatedCornersBotV1.0-F2026"
};
var botFTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [botFTrialCard], "F2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-F2026", 1.5m, freshOdds, now);
if (!botFTrial.CanPublish)
    throw new InvalidOperationException($"Healthy F2026 GOALS controlled trial was blocked: {botFTrial.Reason}");
Equal("ControlledTrial", botFTrial.Tier ?? string.Empty, "F2026 controlled-trial eligibility tier");
EqualDecimal(0.5m, botFTrial.MaxStakeUnits, "F2026 controlled-trial stake cap");
var botFHistoricalGreen = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [botFTrialCard with { PredictiveFixtures = 150, PredictiveResolved = 150, TrafficLight = "Green" }],
    "F2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-F2026", 1.5m, freshOdds, now);
if (!botFHistoricalGreen.CanPublish || botFHistoricalGreen.Tier != "ControlledTrial")
    throw new InvalidOperationException("F2026 historical Green bypassed the controlled-trial tier.");
EqualDecimal(0.5m, botFHistoricalGreen.MaxStakeUnits, "F2026 historical Green remains capped");
var botFWeakHistoricalGreen = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [botFTrialCard with { PredictiveFixtures = 150, PredictiveResolved = 150, TrafficLight = "Green", Yield = 0.0699m }],
    "F2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-F2026", 1.5m, freshOdds, now);
if (botFWeakHistoricalGreen.CanPublish)
    throw new InvalidOperationException("F2026 below 7% yield bypassed the controlled trial through historical Green.");

var botATrialCard = controlledTrialCard with
{
    BotKey = "A",
    AutomationVersion = "AutomatedCornersBotV1.0-A"
};
var botATrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [botATrialCard], "A", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-A", 1.5m, freshOdds, now);
if (botATrial.CanPublish)
    throw new InvalidOperationException("Bot A entered the C2026/F2026 controlled trial without reaching general Green eligibility.");
var botAGreenCard = greenSide with
{
    BotKey = "A",
    AutomationVersion = "AutomatedCornersBotV1.0-A"
};
var botAGreen = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [botAGreenCard], "A", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-A", 1.5m, freshOdds, now);
if (!botAGreen.CanPublish || botAGreen.Tier != "Green")
    throw new InvalidOperationException("Bot A could not enter through the general Green gate after reaching 100 fixtures.");

var unhealthyTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { Yield = 0.0699m }], "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (unhealthyTrial.CanPublish) throw new InvalidOperationException("GOALS below the 7% yield floor entered the controlled trial.");
var minimumYieldTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { Yield = 0.07m }], "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (!minimumYieldTrial.CanPublish) throw new InvalidOperationException("GOALS exactly at the 7% yield floor was blocked.");
var uncalibratedTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { CalibrationGap = 0.051d }], "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (uncalibratedTrial.CanPublish) throw new InvalidOperationException("GOALS outside the calibration limit entered the controlled trial.");
var worseThanMarketTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { DeltaBrier = 0.001d }], "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (worseThanMarketTrial.CanPublish) throw new InvalidOperationException("GOALS with positive delta Brier entered the controlled trial.");
var undersampledTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [controlledTrialCard with { PredictiveFixtures = 29 }], "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, freshOdds, now);
if (undersampledTrial.CanPublish) throw new InvalidOperationException("GOALS with fewer than 30 independent fixtures entered the controlled trial.");

var quarter = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.25m, freshOdds, now);
if (quarter.CanPublish) throw new InvalidOperationException("Quarter line reached binary-EV production.");

var stale = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards, "C2026", "GOALS", "AwayTeamGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 1.5m, now.AddHours(-3), now);
if (stale.CanPublish) throw new InvalidOperationException("Stale odds reached the controlled trial.");

var mutableOdds = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "C2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV1.0-C2026",
    1.5m,
    freshOdds,
    now,
    immutableOddsSnapshotAvailable: false);
if (mutableOdds.CanPublish) throw new InvalidOperationException("Mutable odds reached the controlled trial.");

var alternateBookmaker = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "C2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Betano",
    "AutomatedCornersBotV1.0-C2026",
    1.5m,
    freshOdds,
    now);
if (!alternateBookmaker.CanPublish || alternateBookmaker.Tier != controlledTrial.Tier
    || alternateBookmaker.MaxStakeUnits != controlledTrial.MaxStakeUnits)
    throw new InvalidOperationException("Changing only the bookmaker changed production eligibility.");

var unprovenSide = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "C2026",
    "GOALS",
    "AwayTeamGoals",
    "Under",
    "Pinnacle",
    "AutomatedCornersBotV1.0-C2026",
    1.5m,
    freshOdds,
    now);
if (unprovenSide.CanPublish) throw new InvalidOperationException("A side without its own exact scorecard reached a GOALS trial.");

var unprovenVersion = AutomatedBotProductionEligibilityPolicy.Evaluate(
    controlledTrialCards,
    "C2026",
    "GOALS",
    "AwayTeamGoals",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV2.0-C2026",
    1.5m,
    freshOdds,
    now);
if (unprovenVersion.CanPublish) throw new InvalidOperationException("A new bot version inherited an older GOALS trial scorecard.");

var totalGoalsTrialCard = controlledTrialCard with { MarketType = "TotalGoals" };
var totalGoalsTrial = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [totalGoalsTrialCard], "C2026", "GOALS", "TotalGoals", "Over", "Pinnacle", "AutomatedCornersBotV1.0-C2026", 2.5m, freshOdds, now);
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
Console.WriteLine("PASS the frozen C2026/F2026 AwayTeamGoals Over cohorts enter a 0.5u controlled trial");
Console.WriteLine("PASS the controlled trial requires at least 7% yield and keeps Bot A outside the shortcut");
Console.WriteLine("PASS historical Green cannot promote the controlled C/F cohort above 0.5u automatically");
Console.WriteLine("PASS controlled trial is fail-closed by side/version, freshness, snapshot and market health");
Console.WriteLine("PASS TotalGoals remains paused and non-GOALS retains its family veto");
Console.WriteLine("PASS home goals can publish from their own Green evidence and other scopes report the correct sample threshold");

var publishedCorners = Enumerable.Range(0, 40)
    .Select(index => Pick(
        20_000 + index,
        "D2026",
        "AwayTeamCorners",
        index < 15,
        0.58m,
        0.55m,
        fixtureId: 20_000 + index))
    .ToArray();
var scientificCorners = Enumerable.Range(0, 120)
    .Select(index => Evidence(
        30_000 + index,
        30_000 + index,
        "D2026",
        "AwayTeamCorners",
        index < 70))
    .Append(Evidence(
        40_000,
        30_000,
        "D2026",
        "AwayTeamCorners",
        true))
    .Append(Evidence(
        40_001,
        40_001,
        "D2026",
        "AwayTeamCorners",
        true) with { ModelDecision = "Rejected" })
    .Append(Evidence(
        40_002,
        40_002,
        "D2026",
        "AwayTeamCorners",
        true) with
        {
            DecisionAtUtc = now.AddHours(-1),
            OutcomeAvailableAtUtc = now.AddHours(-2)
        })
    // A selector ranks one research winner per fixture and market family. The
    // GOALS observation must survive alongside CORNERS for the same fixture.
    .Append(Evidence(
        40_003,
        30_000,
        "D2026",
        "AwayTeamGoals",
        true))
    .ToArray();

var scientificService = new AutomatedBotPerformanceService(
    new FakeRepository(rows.Concat(publishedCorners).ToArray()),
    new FakeEvidenceRepository(scientificCorners));
var scientificScorecards = await scientificService.GetScorecardsAsync(CancellationToken.None);
var scientificExact = scientificScorecards.Single(row =>
    row.WindowDays == 30
    && row.Dimension == "BotMarketSideBookmakerVersion"
    && row.BotKey == "D2026"
    && row.MarketType == "AwayTeamCorners"
    && row.SelectedSide == "Over"
    && row.Bookmaker == "Pinnacle"
    && row.AutomationVersion == "AutomatedCornersBotV1.0-D2026");
if (scientificExact.PredictiveResolved != 121 || scientificExact.PredictiveFixtures != 120)
    throw new InvalidOperationException("Repeated scientific runs inflated the independent-fixture sample.");
if (scientificExact.ScientificPredictiveResolved != 121
    || scientificExact.PublishedPredictiveResolved != 0
    || scientificExact.EvidenceBasis != "ScientificEvaluations")
    throw new InvalidOperationException("Published picks were mixed into a shadow-covered exact segment.");
Equal("Green", scientificExact.TrafficLight, "settled scientific segment must become Green");

var sameFixtureGoals = scientificScorecards.Single(row =>
    row.WindowDays == 30
    && row.Dimension == "BotMarketSideBookmakerVersion"
    && row.BotKey == "D2026"
    && row.MarketType == "AwayTeamGoals"
    && row.SelectedSide == "Over"
    && row.Bookmaker == "Pinnacle"
    && row.AutomationVersion == "AutomatedCornersBotV1.0-D2026");
if (sameFixtureGoals.ScientificPredictiveResolved != 1
    || sameFixtureGoals.PredictiveFixtures != 1
    || sameFixtureGoals.EvidenceBasis != "ScientificEvaluations")
    throw new InvalidOperationException("A research winner from another market family was discarded for the same fixture.");

var scientificFamily = scientificScorecards.Single(row =>
    row.WindowDays == 30
    && row.Dimension == "BotFamily"
    && row.BotKey == "D2026"
    && row.MarketFamily == "CORNERS");
var scientificVersion = scientificScorecards.Single(row =>
    row.WindowDays == 30
    && row.Dimension == "BotMarketSideVersion"
    && row.BotKey == "D2026"
    && row.MarketType == "AwayTeamCorners"
    && row.SelectedSide == "Over"
    && row.AutomationVersion == "AutomatedCornersBotV1.0-D2026");
var promotedFromShadow = AutomatedBotProductionEligibilityPolicy.Evaluate(
    [scientificVersion, scientificFamily],
    "D2026",
    "CORNERS",
    "AwayTeamCorners",
    "Over",
    "Pinnacle",
    "AutomatedCornersBotV1.0-D2026",
    4.5m,
    freshOdds,
    now);
if (!promotedFromShadow.CanPublish || promotedFromShadow.Tier != "Green")
    throw new InvalidOperationException($"Settled shadow evidence did not break the publication loop: {promotedFromShadow.Reason}");

Console.WriteLine("PASS settled scientific evidence supersedes published evidence per exact segment");
Console.WriteLine("PASS shadow fixtures can earn Green eligibility without prior publication");
Console.WriteLine("PASS rejected, temporally unsafe and repeated evidence cannot inflate promotion");
Console.WriteLine("PASS one fixture retains independent CORNERS and GOALS research winners");

await BookmakerIndependentPerformanceTests.RunAsync();

var evidenceRepositorySource = ReadRepoFile(
    "CornersPrediction.Infrastructure",
    "SqlServer",
    "SqlServerAutomatedBotPerformanceEvidenceRepository.cs");
var evidenceSql = ExtractRawString(evidenceRepositorySource, "const string sql = \"\"\"");
var sqlParser = new TSql160Parser(initialQuotedIdentifiers: true);
_ = sqlParser.Parse(new StringReader(evidenceSql), out var evidenceSqlErrors);
if (evidenceSqlErrors.Count > 0)
{
    throw new InvalidOperationException(
        "Scientific evidence SQL does not parse: "
        + string.Join(" | ", evidenceSqlErrors.Select(error =>
            $"L{error.Line},C{error.Column}: {error.Message}")));
}
Contains(evidenceRepositorySource, "CREATE TABLE #Candidates", "scientific evidence materializes a narrow ranking ledger");
Contains(evidenceRepositorySource, "evaluation.Decision = N'Approved'", "current approvals use a sargable branch");
Contains(evidenceRepositorySource, "evaluation.Decision = N'Rejected'", "legacy publication rejections retain their fallback branch");
Contains(evidenceRepositorySource, "WHERE candidate.IsResearchWinner = 1", "new evaluations use explicit research winners");
Contains(evidenceRepositorySource, "FROM #ResearchWinnerIds AS winner", "wide evidence is fetched only after candidate ranking");
Contains(evidenceRepositorySource, "SELECT TOP (1)", "official outcomes use an exact bounded fixture lookup");
Contains(
    evidenceRepositorySource,
    "exactHistory.ApiFootballFixtureId = evaluation.ApiFootballFixtureId",
    "official outcomes match the immutable fixture id");
DoesNotContain(
    evidenceRepositorySource,
    "COUNT_BIG(*) OVER (PARTITION BY candidate.EvidenceId)",
    "official fixture lookup must not rebuild a history-wide match-count window");
DoesNotContain(evidenceRepositorySource, "evaluation.PublicationStatus", "scientific evidence cannot depend on publication status");
DoesNotContain(evidenceRepositorySource, "evaluation.PublishedSelectionId", "scientific evidence cannot depend on a published pick");

var matchHistoryIndexes = ReadRepoFile(
    "CornersPredictionApi",
    "SqlScripts",
    "MatchHistoryPerformanceIndexes.sql");
_ = new TSql160Parser(initialQuotedIdentifiers: true)
    .Parse(new StringReader(matchHistoryIndexes), out var matchHistoryIndexSqlErrors);
if (matchHistoryIndexSqlErrors.Count > 0)
{
    throw new InvalidOperationException(
        "MatchHistory performance-index SQL does not parse: "
        + string.Join(" | ", matchHistoryIndexSqlErrors.Select(error =>
            $"L{error.Line},C{error.Column}: {error.Message}")));
}
Contains(
    matchHistoryIndexes,
    "ON dbo.MatchHistory(ApiFootballFixtureId, ApiFootballUpdatedAtUtc DESC, Id DESC)",
    "bounded fixture lookup has an always-initialized fixture-leading index");
Contains(
    matchHistoryIndexes,
    "WHERE ApiFootballFixtureId IS NOT NULL",
    "official fixture evidence index stays filtered and compact");

Console.WriteLine("PASS scientific scorecard SQL ranks a narrow ledger before exact indexed outcome lookup");
Console.WriteLine("PASS scientific scorecard SQL remains independent from publication state");
Console.WriteLine("PASS scientific scorecard SQL parses as SQL Server 2022 syntax");

var botReadIndexes = ReadRepoFile(
    "CornersPredictionApi",
    "SqlScripts",
    "BotAutomationReadIndexes.sql");
_ = new TSql160Parser(initialQuotedIdentifiers: true)
    .Parse(new StringReader(botReadIndexes), out var botReadIndexSqlErrors);
if (botReadIndexSqlErrors.Count > 0)
{
    throw new InvalidOperationException(
        "Bot read-index SQL does not parse: "
        + string.Join(" | ", botReadIndexSqlErrors.Select(error =>
            $"L{error.Line},C{error.Column}: {error.Message}")));
}
Contains(
    botReadIndexes,
    "IX_AutomatedBotPickEvaluations_PerformanceCandidates",
    "scientific candidate ranking has an always-initialized covering index");
Contains(
    evidenceRepositorySource,
    "evaluation.PerformanceLegacyPublicationRejection = 1",
    "legacy scorecard candidates seek the stored compatibility predicate");
Contains(
    botReadIndexes,
    "DecisionReasonsJson LIKE N''%REJECTED_PRODUCTION_GATE%''",
    "the persisted legacy flag retains the original production-rejection predicate");
Contains(
    botReadIndexes,
    "DecisionReasonsJson LIKE N''%REJECTED_LOWER_RANKED_CANDIDATE%''",
    "the persisted legacy flag retains the original lower-ranked predicate");
Console.WriteLine("PASS filtered scorecard candidate index parses as SQL Server 2022 syntax");

AutomatedBotPerformanceScorecard Find(string bot, string market, int window) => scorecards.Single(row =>
    row.WindowDays == window
    && row.Dimension == "BotMarketType"
    && row.BotKey == bot
    && row.MarketType == market);

AutomatedBotPerformanceScorecard FindSideVersion(
    string bot,
    string market,
    string side,
    string automationVersion,
    int window) => scorecards.Single(row =>
    row.WindowDays == window
    && row.Dimension == "BotMarketSideVersion"
    && row.BotKey == bot
    && row.MarketType == market
    && row.SelectedSide == side
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

AutomatedBotPerformanceEvidence Evidence(
    long id,
    long fixtureId,
    string bot,
    string market,
    bool won) => new()
{
    EvidenceId = id,
    EvidenceKey = $"evaluation:{id}",
    ApiFootballFixtureId = fixtureId,
    BotKey = bot,
    AutomationVersion = $"AutomatedCornersBotV1.0-{bot}",
    FixtureDateUtc = now.AddDays(-2),
    League = "Test League",
    HomeTeam = $"Home {fixtureId}",
    AwayTeam = $"Away {fixtureId}",
    Bookmaker = "Pinnacle",
    MarketType = market,
    SelectedSide = "Over",
    LineValue = 4.5m,
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

static string ReadRepoFile(params string[] relativeSegments)
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        var path = Path.Combine([directory.FullName, .. relativeSegments]);
        if (File.Exists(path))
            return File.ReadAllText(path);
    }

    throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativeSegments)}");
}

static string ExtractRawString(string source, string marker)
{
    var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
    if (markerIndex < 0)
        throw new InvalidOperationException($"Could not find raw-string marker '{marker}'.");

    var contentStart = source.IndexOf('\n', markerIndex);
    if (contentStart < 0)
        throw new InvalidOperationException("Raw SQL string has no content line.");

    var contentEnd = source.IndexOf("\"\"\";", contentStart, StringComparison.Ordinal);
    if (contentEnd < 0)
        throw new InvalidOperationException("Raw SQL string has no closing delimiter.");

    return source[(contentStart + 1)..contentEnd];
}

static void Contains(string value, string expected, string message)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: missing '{expected}'.");
}

static void DoesNotContain(string value, string unexpected, string message)
{
    if (value.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: found forbidden '{unexpected}'.");
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

sealed class FakeEvidenceRepository(IReadOnlyList<AutomatedBotPerformanceEvidence> rows)
    : IAutomatedBotPerformanceEvidenceRepository
{
    public Task<IReadOnlyList<AutomatedBotPerformanceEvidence>> GetSettledEvidenceAsync(
        DateTime fixtureFromUtc,
        DateTime fixtureToUtc,
        DateTime asOfUtc,
        CancellationToken cancellationToken) => Task.FromResult(rows);
}
