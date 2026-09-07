using System.Text.RegularExpressions;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Infrastructure.SqlServer;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var families = new Dictionary<string, string[]>
{
    ["CORNERS"] = ["TotalCorners", "HomeTeamCorners", "AwayTeamCorners"],
    ["GOALS"] = ["TotalGoals", "HomeTeamGoals", "AwayTeamGoals"],
    ["SHOTS"] = ["TotalShots", "HomeTeamShots", "AwayTeamShots"],
    ["SOG"] = ["TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal"]
};
foreach (var (family, expected) in families)
    Equal(string.Join(',', expected), string.Join(',', AutomatedBotMarketScope.MarketTypes($" {family.ToLowerInvariant()} ")), family);
Equal("SOG", AutomatedBotMarketScope.Normalize(" shots_on_goal ")!, "SOG alias");
Check(AutomatedBotMarketScope.Normalize(" ") is null, "Empty family should remain optional.");
Throws(() => AutomatedBotMarketScope.Normalize("CARDS"), "Unknown family");
Equal(12, families.SelectMany(pair => pair.Value).Distinct().Count(), "Families must not overlap");
Console.WriteLine("PASS all four families retain total, home and away scopes without overlap");

var repository = new CapturingRepository();
var useCase = new GetAutomatedCornerSelectionsUseCase(repository);
var request = new AutomatedCornerSelectionsFilterRequest(
    new DateTime(2026, 9, 1, 14, 0, 0), new DateTime(2026, 9, 6, 23, 0, 0),
    " Pending ", " League A ", " Pinnacle ", " HomeTeamGoals ", true, " goals ");
await useCase.GetAsync(request, CancellationToken.None);
var filter = repository.LastFilter!;
Equal(new DateTime(2026, 9, 1), filter.DateFrom!.Value, "DateFrom normalization");
Equal(new DateTime(2026, 9, 6), filter.DateTo!.Value, "DateTo normalization");
Equal("GOALS", filter.MarketFamily!, "Family propagation");
Equal("HomeTeamGoals", filter.MarketType!, "Exact local scope propagation");
Equal("League A", filter.League!, "League propagation");
Equal("Pinnacle", filter.Source!, "Source propagation");
Check(filter.OnlyPending && filter.Status == "Pending", "Pending filters must survive normalization.");
await ThrowsAsync(() => useCase.GetAsync(request with { MarketFamily = "CARDS" }, CancellationToken.None), "Invalid family");
await ThrowsAsync(() => useCase.GetAsync(request with { DateFrom = request.DateTo!.Value.AddDays(1) }, CancellationToken.None), "Reversed date window");
Console.WriteLine("PASS use-case preserves date, league, source, status and exact scope with normalized family");

var summary = new AutomatedBotMonthlySummary { ProfitLoss = 1m, SettledStake = 4m };
Equal(25m, summary.YieldPct!.Value, "Monthly yield percentage");
Check((summary with { SettledStake = 0m }).YieldPct is null, "Unsettled months must not invent a yield.");
Console.WriteLine("PASS monthly yield uses settled stake and stays absent without settled stake");

if (!args.Contains("--sql"))
{
    Console.WriteLine("SQL integration checks not requested; pass --sql with BOT_DASHBOARD_TEST_CONNECTION_STRING to run session-local fixture checks.");
    return;
}

var connectionString = Environment.GetEnvironmentVariable("BOT_DASHBOARD_TEST_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("BOT_DASHBOARD_TEST_CONNECTION_STRING is required for SQL integration checks.");

// Exercise the compiled repository before the synthetic predicate fixtures. A
// replacement SELECT prefix can accidentally hide a broken concatenation such
// as "FROM ... sWHERE" and does not validate the real projection columns.
// This read-only probe deliberately targets a nonexistent league and writes no
// persistent rows; the fixture below separately verifies the returned contents.
var sqlRepository = new SqlServerAutomatedCornerSelectionsRepository(
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = connectionString
    }).Build());
foreach (var family in families.Keys)
{
    var noRows = await sqlRepository.GetSelectionsAsync(
        new AutomatedCornerSelectionsFilterRequest(
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 1), null,
            "__BOT_DASHBOARD_SQL_REGRESSION_NONEXISTENT_LEAGUE__", null, null, false, family),
        CancellationToken.None);
    Equal(0, noRows.Count, $"Compiled repository SQL projection and concatenation for {family}");
}
Console.WriteLine("PASS compiled repository executes its complete selection SQL for all four families");

var root = FindRoot();
var source = File.ReadAllText(Path.Combine(root, "CornersPrediction.Infrastructure/SqlServer/SqlServerAutomatedCornerSelectionsRepository.cs"));
var monthlyMethod = source[source.IndexOf("GetMonthlyHistoryAsync", StringComparison.Ordinal)..source.IndexOf("public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync", StringComparison.Ordinal)];
var monthlyMatch = Regex.Match(monthlyMethod, "const string sql = \"\"\"(?<sql>[\\s\\S]*?)\"\"\";");
Check(monthlyMatch.Success, "Could not locate actual monthly SQL.");
var monthlySql = monthlyMatch.Groups["sql"].Value.Replace("dbo.AutomatedCornerBetSelections", "#DashboardSelections", StringComparison.Ordinal);
var scopedMethod = source[source.IndexOf("public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync", StringComparison.Ordinal)..];
var scopedMatch = Regex.Match(scopedMethod, "const string sql = SelectSelectionSql \\+ \" \" \\+ \"\"\"(?<sql>[\\s\\S]*?)\"\"\";");
Check(scopedMatch.Success, "Could not locate actual scoped selection SQL.");
var scopedSql = "SELECT s.AutomatedCornerBetSelectionId FROM #DashboardSelections s " + scopedMatch.Groups["sql"].Value;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await connection.ExecuteAsync("""
    CREATE TABLE #DashboardSelections
    (
        AutomatedCornerBetSelectionId BIGINT NOT NULL,
        MatchDate DATETIME2 NOT NULL,
        UpdatedAtUtc DATETIME2 NOT NULL DEFAULT '20260906',
        BotKey NVARCHAR(50) NULL,
        AutomationVersion NVARCHAR(100) NOT NULL,
        DecisionReason NVARCHAR(MAX) NULL,
        MarketType NVARCHAR(50) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        Stake DECIMAL(12,4) NOT NULL,
        ProfitLoss DECIMAL(12,4) NULL,
        League NVARCHAR(100) NOT NULL DEFAULT N'Raw League',
        StandardizedLeague NVARCHAR(100) NULL DEFAULT N'League A',
        Source NVARCHAR(50) NOT NULL DEFAULT N'Pinnacle'
    );
    INSERT INTO #DashboardSelections
        (AutomatedCornerBetSelectionId, MatchDate, BotKey, AutomationVersion, DecisionReason, MarketType, Status, Stake, ProfitLoss)
    VALUES
        (1, '20260901', '', 'v-C2026', NULL, 'HomeTeamGoals', 'Won', 2, 1.5),
        (2, '20260902', '', 'v-C2026', NULL, 'AwayTeamGoals', 'Lost', 1, -1),
        (3, '20260903', '', 'v-C2026', NULL, 'TotalGoals', 'Push', 3, 0),
        (4, '20260904', '', 'v-C2026', NULL, 'TotalGoals', 'Void', 9, 0),
        (5, '20260906 23:59:59.999', '', 'v-C2026', NULL, 'HomeTeamGoals', 'Pending', 8, NULL),
        (6, '20260831 23:59:59.999', '', 'v-C2026', NULL, 'HomeTeamGoals', 'Won', 100, 100),
        (7, '20260907', '', 'v-C2026', NULL, 'HomeTeamGoals', 'Won', 100, 100),
        (8, '20260902', '', 'v-C2026', NULL, 'TotalCorners', 'Won', 100, 100),
        (9, '20260902', '', 'v-C2026', NULL, 'HomeTeamShots', 'Won', 100, 100),
        (10, '20260902', '', 'v-C2026', NULL, 'AwayTeamShotsOnGoal', 'Won', 100, 100),
        (11, '20260902', '', 'v-B', NULL, 'TotalGoals', 'Won', 100, 100),
        (12, '20260902', '', 'v-B', NULL, 'TotalGoals', 'Pending', 4, NULL),
        (13, '20260902', ' B ', 'v-A', NULL, 'TotalGoals', 'Won', 100, 100),
        (14, '20260902', '', 'v-A', '{"botProfile":" B "}', 'TotalGoals', 'Lost', 100, -100),
        (15, '20260902', '', 'custom', '{"botProfile":" d2026 "}', 'HomeTeamGoals', 'Won', 1, 0.8),
        (16, '20260902', '', 'custom', '{"botProfile":"E"}', 'AwayTeamGoals', 'Lost', 2, -2),
        (17, '20260902', '', 'custom', '{"botProfile":" F2026 "}', 'TotalGoals', 'Push', 3, 0),
        (18, '20260902', '', 'custom', '{"botProfile":"personal"}', 'HomeTeamGoals', 'Void', 10, 0),
        (19, '20260902', '', 'custom', 'invalid json', 'AwayTeamGoals', 'Pending', 5, NULL),
        (20, '20260902', '', 'custom', '{"botProfile":null}', 'TotalGoals', 'Pending', 6, NULL),
        (21, '20260902', '', 'custom', '{}', 'HomeTeamGoals', 'Pending', 7, NULL),
        (22, '20260902', '', 'v-A', '{"botProfile":"C2026"}', 'AwayTeamGoals', 'Won', 2, 1),
        (23, '20260902', '', 'custom', '{"BotProfile":"B"}', 'TotalGoals', 'Pending', 1, NULL),
        (24, '20260902', 'B', 'v-A', NULL, 'HomeTeamGoals', 'Pending', 1, NULL),
        (25, '20260803', '', 'v-C2026', NULL, 'HomeTeamGoals', 'Won', 1, 0.6);
    """);

var monthly = (await connection.QueryAsync<AutomatedBotMonthlySummary>(monthlySql, new
{
    DateFrom = new DateTime(2026, 9, 1), DateToExclusive = new DateTime(2026, 9, 7),
    MarketTypes = AutomatedBotMarketScope.MarketTypes("GOALS")
})).ToArray();
Equal("A,B,C,D,E,F,Legacy", string.Join(',', monthly.Select(row => row.BotKey)), "Monthly bot ordering");
var c = monthly.Single(row => row.BotKey == "C");
Equal(5, c.Total, "All three goals scopes and included date endpoints");
Equal(1, c.Won, "Won count"); Equal(1, c.Lost, "Lost count"); Equal(1, c.Push, "Push count");
Equal(1, c.Void, "Void count"); Equal(1, c.Pending, "Pending count");
Equal(6m, c.SettledStake, "Only Won/Lost/Push contribute stake");
Equal(0.5m, c.ProfitLoss, "Monthly profit");
Equal(0.5m / 6m * 100m, c.YieldPct!.Value, "SQL-mapped monthly yield");
Equal(new DateTime(2026, 9, 1), c.Month, "Month key");
var b = monthly.Single(row => row.BotKey == "B");
Equal(1, b.Total, "Retired B keeps only pending"); Equal(1, b.Pending, "Retired pending count");
Check(b.YieldPct is null && b.SettledStake == 0m, "Pending B must not contribute settled stake or yield.");
var a = monthly.Single(row => row.BotKey == "A");
Equal(5, a.Total, "Malformed/missing/case-sensitive profile fallback and version precedence");
Equal(4, a.Pending, "Stored retired key remains visible while pending");
Equal(2m, a.SettledStake, "Retired resolved rows excluded even if suffix indicates active A");
Equal(2, monthly.Single(row => row.BotKey == "Legacy").Total, "Custom and explicit null profile stay Legacy");
Equal(0.8m, monthly.Single(row => row.BotKey == "D").ProfitLoss, "Normalized D profile");
Equal(-2m, monthly.Single(row => row.BotKey == "E").ProfitLoss, "Normalized E profile");
Equal(3m, monthly.Single(row => row.BotKey == "F").SettledStake, "Normalized F profile");
Console.WriteLine("PASS actual monthly SQL preserves settlements, retired B visibility, bot classification and yields");

var spanning = (await connection.QueryAsync<AutomatedBotMonthlySummary>(monthlySql, new
{
    DateFrom = new DateTime(2026, 8, 1), DateToExclusive = new DateTime(2026, 9, 7),
    MarketTypes = AutomatedBotMarketScope.MarketTypes("GOALS")
})).ToArray();
Check(spanning[0].Month == new DateTime(2026, 9, 1) && spanning[^1].Month == new DateTime(2026, 8, 1), "Months must remain separate and descending.");
Console.WriteLine("PASS actual monthly SQL separates months and sorts newest first");

await connection.ExecuteAsync("""
    UPDATE #DashboardSelections SET StandardizedLeague = N'League B' WHERE AutomatedCornerBetSelectionId = 15;
    UPDATE #DashboardSelections SET Source = N'Betano' WHERE AutomatedCornerBetSelectionId = 16;
    UPDATE #DashboardSelections SET StandardizedLeague = NULL, League = N'Fallback League' WHERE AutomatedCornerBetSelectionId = 17;
    """);
async Task<long[]> SelectIds(string family, string? marketType = null, string? league = "League A", string? status = null, bool onlyPending = false, string? bookmaker = "Pinnacle") =>
    (await connection.QueryAsync<long>(scopedSql, new
    {
        DateFrom = new DateTime(2026, 9, 1), DateToExclusive = new DateTime(2026, 9, 7),
        Status = status, League = league, Source = bookmaker, MarketType = marketType, OnlyPending = onlyPending,
        MarketTypes = AutomatedBotMarketScope.MarketTypes(family)
    })).ToArray();

var scopedGoals = await SelectIds("GOALS");
Check(new long[] { 1, 2, 3 }.All(scopedGoals.Contains), "GOALS family must retain home, away and total.");
Check(new long[] { 6, 7, 8, 9, 10, 15, 16, 17, 25 }.All(id => !scopedGoals.Contains(id)), "Date/league/source/family boundaries must exclude unrelated rows.");
Equal("2,19,22", string.Join(',', (await SelectIds("GOALS", "AwayTeamGoals")).OrderBy(id => id)), "Exact scope must intersect with family");
Equal(0, (await SelectIds("GOALS", "TotalCorners")).Length, "Cross-family exact scope must return no rows");
Equal("8", string.Join(',', await SelectIds("CORNERS")), "Corners separation");
Equal("9", string.Join(',', await SelectIds("SHOTS")), "Shots separation");
Equal("10", string.Join(',', await SelectIds("SOG")), "SOG separation");
Equal("17", string.Join(',', await SelectIds("GOALS", league: "Fallback League")), "Raw league fallback");
var pending = await SelectIds("GOALS", onlyPending: true);
Check(pending.Contains(5) && pending.Contains(12) && !pending.Contains(1), "OnlyPending must preserve current endpoint and exclude settled rows.");
Equal(0, (await SelectIds("GOALS", status: "Won", onlyPending: true)).Length, "Conflicting status must not leak rows");
Console.WriteLine("PASS actual scoped selection SQL enforces family, scope, date, league, source and pending intersections");

static string FindRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "CornersPrediction.Infrastructure")))
        directory = directory.Parent;
    return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message) =>
    Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}.");

static void Throws(Action action, string name)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new InvalidOperationException($"{name} should fail validation.");
}

static async Task ThrowsAsync(Func<Task> action, string name)
{
    try { await action(); } catch (ArgumentException) { return; }
    throw new InvalidOperationException($"{name} should fail validation.");
}

sealed class CapturingRepository : IAutomatedCornerSelectionsRepository
{
    public AutomatedCornerSelectionsFilterRequest? LastFilter { get; private set; }
    public Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(AutomatedCornerSelectionsFilterRequest filters, CancellationToken cancellationToken)
    {
        LastFilter = filters;
        return Task.FromResult<IReadOnlyList<AutomatedCornerSelectionDto>>([]);
    }
    public Task<AutomatedCornerSelectionDto> UpdateStatusAsync(long id, UpdateAutomatedCornerSelectionStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AutomatedCornerSelectionDto> ResolveAsync(long id, int actualValue, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AutomatedCornerSelectionDto> LinkMatchAsync(long id, long matchHistoryId, long apiFootballFixtureId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotSupportedException();
}
