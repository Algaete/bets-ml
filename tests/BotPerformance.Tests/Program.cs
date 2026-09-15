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

foreach (var card in scorecards)
{
    if (card.ProductionBlocked != (card.Yield is null or < 0.03m))
        throw new InvalidOperationException("Scorecard blocking flag differs from the yield policy.");
}
var smallSample = await new AutomatedBotPerformanceService(new FakeRepository(
    [Pick(999_001, "D2026", "AwayTeamGoals", true, 0.95m, 0.55m)]))
    .GetScorecardsAsync(CancellationToken.None);
var grayExact = smallSample.Single(card => card.WindowDays == 30 && card.Dimension == "BotMarketSideVersion");
Equal("Gray", grayExact.TrafficLight, "Small sample retains diagnostic color");
if (grayExact.ProductionBlocked || !grayExact.Recommendation.Contains("Cumple rendimiento", StringComparison.Ordinal))
    throw new InvalidOperationException("A small profitable segment received an obsolete sample-based blocking message.");
Console.WriteLine("PASS scorecards expose yield-based blocking and recommendation independently from diagnostic color");

var freshOdds = now.AddMinutes(-10);
ProductionYieldPolicyTests.Run(greenSide, now);

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
if (!promotedFromShadow.CanPublish || promotedFromShadow.Tier != "Yield")
    throw new InvalidOperationException($"Settled shadow evidence did not break the publication loop: {promotedFromShadow.Reason}");

Console.WriteLine("PASS settled scientific evidence supersedes published evidence per exact segment");
Console.WriteLine("PASS shadow fixtures can qualify by yield without prior publication");
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
    botReadIndexes,
    "IX_AutomatedCornerBetSelections_PerformanceWindow",
    "published scorecards have a covering 90-day read index");
Contains(
    botReadIndexes,
    "IX_CornerOddsSnapshots_SourceMatchMarketCapture",
    "live odds resolve snapshots by provider match identity");
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
