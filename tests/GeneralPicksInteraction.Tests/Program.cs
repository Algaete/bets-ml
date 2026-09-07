using System.Reflection;
using System.Transactions;
using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Infrastructure.SqlServer;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.TransactSql.ScriptDom;

var builder = typeof(SqlServerAutomatedBotResearchRepository).GetMethod("BuildGeneralPicksQuery", BindingFlags.NonPublic | BindingFlags.Static)!;
foreach (var column in AutomatedBotGeneralPickSorting.Columns)
foreach (var direction in new[] { "asc", "desc" })
foreach (var filtered in new[] { false, true })
{
    var query = GetAutomatedBotGeneralPicksUseCase.Normalize(new(null, null, "GOALS", null, null,
        filtered ? "Approved" : null, null, SortBy: column, SortDirection: direction));
    var sql = (string)builder.Invoke(null, [query])!;
    new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
    Check(errors.Count == 0, $"Invalid SQL for {column}/{direction}: {string.Join(';', errors.Select(e => $"{e.Line}: {e.Message}"))}");
    if (column == "OutcomeStatus")
    {
        var projection = sql[..sql.IndexOf("INTO #GeneralOutcomeSort", StringComparison.Ordinal)];
        Check(!projection.Contains("evaluation.FeatureSnapshotJson"), "Sorting scanned the wide audit snapshot.");
    }
}
var labSql = (string)typeof(SqlServerAutomatedBotResearchRepository)
    .GetField("GeneralPickLabSql", BindingFlags.NonPublic | BindingFlags.Static)!
    .GetRawConstantValue()!;
new TSql160Parser(true).Parse(new StringReader(labSql), out var labErrors);
Check(labErrors.Count == 0,
    $"Invalid General Picks lab SQL: {string.Join(';', labErrors.Select(error => $"{error.Line}: {error.Message}"))}");
Check(!labSql.Contains("FeatureSnapshotJson", StringComparison.Ordinal),
    "The lab query must not scan feature snapshots.");
Check(labSql.Contains("SignalSequence = ROW_NUMBER()", StringComparison.Ordinal)
    && labSql.Contains("BinaryOutcome", StringComparison.Ordinal)
    && labSql.Contains("CumulativeProfitLossUnits", StringComparison.Ordinal),
    "The lab must deduplicate signals and calculate outcome/calibration time series.");
var labCapture = new CaptureLabRepository();
await new GetGeneralPickLabUseCase(labCapture).GetAsync(
    new(null, null, "goals", "HomeTeamGoals", "c", "Rejected", "ProductionBlocked"), default);
Check(labCapture.Query is { ModelDecision: "Approved", MarketFamily: "GOALS",
    MarketType: "HomeTeamGoals", BotKey: "C2026", PublicationStatus: "ProductionBlocked" },
    "The lab must force approved model decisions while preserving current scientific slices.");
Throws(() => GetAutomatedBotGeneralPicksUseCase.Normalize(new(null, null, null, null, null, null, null,
    SortBy: "MatchDate; DROP TABLE anything")));
Throws(() => GetAutomatedBotGeneralPicksUseCase.Normalize(new(null, null, null, null, null, null, null,
    SortDirection: "desc; SELECT 1")));
foreach (var value in new int?[] { null, -1, 1001 })
    Throws(() => GeneralPickManualSettlement.Validate(1, new(value, "Source", Guid.NewGuid()), "tester"));
GeneralPickManualSettlement.Validate(1, new(0, "Result confirmed", Guid.NewGuid()), "tester");
Throws(() => GeneralPickManualSettlement.Validate(1, new(0, " ", Guid.NewGuid()), "tester"));
Throws(() => GeneralPickManualSettlement.Validate(1, new(0, "Source", Guid.Empty), "tester"));
Throws(() => GeneralPickManualSettlement.Validate(1, new(0, "Source", Guid.NewGuid()), ""));
foreach (var (side, line, actual, expected) in new[] {
    ("Over", .25m, 0, -.5m), ("Under", .25m, 0, .5m), ("Over", .75m, 1, .5m),
    ("Under", .75m, 1, -.5m), ("Over", 1m, 1, 0m), ("Under", .5m, 0, 1m) })
    Check(AutomatedBotPickSettlementCalculator.Calculate(side, line, actual, 1.9m, 1m).Factor == expected,
        "Incorrect manual Asian settlement.");
Console.WriteLine("PASS all column/direction/filter SQL variants, injection rejection, manual validation and Asian outcomes.");
if (!args.Contains("--sql")) return;

var root = Directory.GetCurrentDirectory();
var connectionString = File.ReadLines(Path.Combine(root, ".env"))
    .First(line => line.StartsWith("AZURE_SQL_CONNECTION_STRING=")).Split('=', 2)[1].Trim().Trim('"', '\'');
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
var reader = new SqlServerAutomatedBotResearchRepository(configuration);
var writer = new SqlServerGeneralPickManualSettlementRepository(configuration);
var end = DateTime.UtcNow.Date.AddDays(-1);
var scopeQuery = new AutomatedBotResearchQuery(end.AddDays(-5), end.AddDays(1), "GOALS", null,
    "F2026", "Approved", null, 1, 20);
var labWatch = System.Diagnostics.Stopwatch.StartNew();
var liveLab = await reader.GetLabAsync(scopeQuery, default);
labWatch.Stop();
Check(liveLab.Summary.ApprovedEvaluations >= liveLab.Summary.IndependentSignals,
    "Lab deduplication increased the approved population.");
Check(liveLab.Summary.IndependentSignals == liveLab.Summary.ResolvedSignals
    + liveLab.Summary.PendingSignals + liveLab.Summary.UnavailableSignals + liveLab.Summary.VoidSignals,
    "Lab outcome coverage does not account for every independent signal.");
if (liveLab.Timeline.Count > 0)
    Check(liveLab.Timeline[^1].CumulativeProfitLossUnits == liveLab.Summary.ProfitLossUnits,
        "Lab cumulative P/L does not reconcile with its summary.");
Console.WriteLine($"PASS SQL approved lab: elapsedMs={labWatch.ElapsedMilliseconds}; approved={liveLab.Summary.ApprovedEvaluations}; independent={liveLab.Summary.IndependentSignals}; resolved={liveLab.Summary.ResolvedSignals}; timelineDays={liveLab.Timeline.Count}; bins={liveLab.Calibration.Count}; segments={liveLab.Segments.Count}.");
foreach (var (column, direction) in new[] { ("SelectedOdds", "asc"), ("SelectedOdds", "desc"), ("OutcomeStatus", "asc"), ("FinalProbability", "desc") })
{
    var first = await reader.GetGeneralPicksAsync(scopeQuery with { SortBy = column, SortDirection = direction }, default);
    var second = await reader.GetGeneralPicksAsync(scopeQuery with { SortBy = column, SortDirection = direction, Page = 2 }, default);
    var both = first.Items.Concat(second.Items).ToArray();
    Check(both.DistinctBy(row => row.EvaluationId).Count() == both.Length, "Repeated row across sorted pages.");
    for (var i = 1; i < both.Length; i++)
    {
        var comparison = column == "OutcomeStatus" ? StringComparer.OrdinalIgnoreCase.Compare(both[i-1].OutcomeStatus, both[i].OutcomeStatus)
            : CompareNullable(column == "SelectedOdds" ? both[i-1].SelectedOdds : both[i-1].FinalProbability,
                column == "SelectedOdds" ? both[i].SelectedOdds : both[i].FinalProbability, direction);
        Check(column == "OutcomeStatus" ? comparison <= 0 : comparison <= 0, $"Global {column}/{direction} ordering failed.");
    }
    Console.WriteLine($"PASS SQL global {column}/{direction}: {both.Length} rows across pages, {first.TotalCount} total.");
}
var candidates = await reader.GetGeneralPicksAsync(scopeQuery with { SortBy = "EvaluationId", SortDirection = "desc", PageSize = 100 }, default);
var candidate = candidates.Items.First(row => row.PublishedSelectionId is null && row.SelectedSide is "Over" or "Under" && row.SelectedOdds > 1);
var request = new GeneralPickManualSettlementRequest(0, "Rollback integration check", Guid.NewGuid());
var originalAudit = await Scalar<string>("SELECT * FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", new { Id = candidate.EvaluationId });
using (var scope = NewScope())
{
    var saved = await writer.SettleAsync(candidate.EvaluationId, request, "integration-test", default);
    var retried = await writer.SettleAsync(candidate.EvaluationId, request, "integration-test", default);
    Check(saved == retried, "A retry changed the manual settlement.");
    var refreshed = await reader.GetGeneralPicksAsync(scopeQuery with { SortBy = "EvaluationId", SortDirection = "desc", PageSize = 100 }, default);
    var row = refreshed.Items.Single(row => row.EvaluationId == candidate.EvaluationId);
    Check(row.OutcomeSource == "Manual" && row.ActualValue == 0 && row.ManualSettledBy == "integration-test", "Manual outcome did not survive reload.");
    Check(await Scalar<int>("SELECT COUNT(*) FROM dbo.GeneralBotPickManualSettlements WHERE RequestId=@RequestId", request) == 1, "Retry duplicated the audit.");
    Check(originalAudit == await Scalar<string>("SELECT * FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", new { Id = candidate.EvaluationId }), "Manual settlement changed the model audit.");
    // Intentionally no Complete(): every write is rolled back.
}
Check(await Scalar<int>("SELECT COUNT(*) FROM dbo.GeneralBotPickManualSettlements WHERE RequestId=@RequestId", request) == 0, "Test left a manual result behind.");
Console.WriteLine("PASS SQL unpublished manual settlement, reload, zero, audit identity, idempotent retry and full rollback.");

var publishedId = await Scalar<long>("SELECT TOP (1) AutomatedCornerBetSelectionId FROM dbo.AutomatedCornerBetSelections WHERE MatchDate < @End AND BotKey=N'F2026' AND SelectedSide IN(N'Over',N'Under') AND Odds>1 ORDER BY AutomatedCornerBetSelectionId DESC", new { End = end });
var originalSelection = await Scalar<string>("SELECT * FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", new { Id = publishedId });
using (var scope = NewScope())
{
    await writer.SettleAsync(-publishedId, request with { RequestId = Guid.NewGuid() }, "integration-test", default);
    Check(await Scalar<string>("SELECT SettlementSource FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id", new { Id = publishedId }) == "Manual", "Published settlement was not updated.");
}
Check(originalSelection == await Scalar<string>("SELECT * FROM dbo.AutomatedCornerBetSelections WHERE AutomatedCornerBetSelectionId=@Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER", new { Id = publishedId }), "Published test did not roll back completely.");
Console.WriteLine("PASS SQL published settlement stays synchronized; complete row restored after rollback.");

async Task<T> Scalar<T>(string sql, object parameters)
{
    await using var connection = new SqlConnection(connectionString);
    return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, commandTimeout: 90)))!;
}
static TransactionScope NewScope() => new(TransactionScopeOption.Required,
    new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(5) }, TransactionScopeAsyncFlowOption.Enabled);
static int CompareNullable(decimal? left, decimal? right, string direction) => !left.HasValue ? right.HasValue ? 1 : 0
    : !right.HasValue ? -1 : direction == "asc" ? left.Value.CompareTo(right.Value) : right.Value.CompareTo(left.Value);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection."); }

sealed class CaptureLabRepository : IGeneralPickLabRepository
{
    public AutomatedBotResearchQuery? Query { get; private set; }

    public Task<GeneralPickLab> GetLabAsync(AutomatedBotResearchQuery query, CancellationToken cancellationToken)
    {
        Query = query;
        return Task.FromResult(new GeneralPickLab(
            new GeneralPickLabSummary(), [], [], [], DateTime.UtcNow));
    }
}
