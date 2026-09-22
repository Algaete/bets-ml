using Dapper;
using Microsoft.Data.SqlClient;

if (!args.Contains("--sql"))
{
    Console.WriteLine("Use --sql to test the lab query in an isolated schema with a full rollback.");
    return 0;
}

var root = new DirectoryInfo(Environment.CurrentDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "CornersPrediction.sln"))) root = root.Parent;
if (root is null) throw new InvalidOperationException("Run from the repository.");
var connectionString = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = File.ReadLines(Path.Combine(root.FullName, ".env"))
        .First(line => line.StartsWith("AZURE_SQL_CONNECTION_STRING="))
        .Split('=', 2)[1].Trim().Trim('"', '\'');

var source = File.ReadAllText(Path.Combine(root.FullName, "CornersPredictionApi/sql/20260831_bot_i_shadow_market_movement.sql"));
var start = source.IndexOf("CREATE OR ALTER FUNCTION dbo.fn_BotI2026ShadowLab", StringComparison.Ordinal);
var end = source.IndexOf("\nGO", start, StringComparison.Ordinal);
var schema = "LabIRegression_" + Guid.NewGuid().ToString("N");
var function = source[start..end]
    .Replace("dbo.fn_BotI2026ShadowLab", $"[{schema}].fn_BotI2026ShadowLab")
    .Replace("dbo.BotI2026ShadowEvaluations", $"[{schema}].BotI2026ShadowEvaluations")
    .Replace("dbo.MatchHistory", $"[{schema}].MatchHistory")
    .Replace("dbo.TeamNameAlias", $"[{schema}].TeamNameAlias");

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
try
{
    await connection.ExecuteAsync($"CREATE SCHEMA [{schema}];", transaction: transaction);
    var fixture = File.ReadAllText(Path.Combine(root.FullName, "tests/BotILabResolution.Tests/fixture.sql"))
        .Replace("[LabITest]", $"[{schema}]");
    await connection.ExecuteAsync(fixture, transaction: transaction, commandTimeout: 60);
    await connection.ExecuteAsync(function, transaction: transaction, commandTimeout: 60);
    var rows = (await connection.QueryAsync<Outcome>($"SELECT * FROM [{schema}].fn_BotI2026ShadowLab('2026-09-22T03:00:00');", transaction: transaction, commandTimeout: 60)).ToDictionary(row => row.ShadowEvaluationId);
    var expected = new Dictionary<long, string>
    {
        [1] = "Settled", [2] = "Settled", [3] = "Ambiguous", [4] = "OfficialFixtureMissing",
        [5] = "TemporalRejected", [6] = "Pending", [7] = "TemporalRejected", [8] = "Pending",
        [9] = "NotSelected", [10] = "Pending", [11] = "Ambiguous", [12] = "OfficialFixtureMissing",
        [13] = "Settled", [14] = "Settled", [15] = "OfficialFixtureMissing", [16] = "Settled", [17] = "OfficialFixtureMissing"
    };
    if (rows.Count != expected.Count) throw new Exception("Identity joins duplicated or dropped observations.");
    foreach (var (id, state) in expected)
    {
        if (rows[id].SettlementState != state) throw new Exception($"Case {id}: expected {state}, got {rows[id].SettlementState}.");
        if (state != "Settled" && (rows[id].Result is not null || rows[id].ProfitLoss is not null))
            throw new Exception($"Case {id} leaked unverified economics.");
    }
    if (rows[2].ApiFootballFixtureId is not null || rows[2].ResolvedApiFootballFixtureId != 101)
        throw new Exception("Resolved identity must remain separate from immutable audit identity.");
    if (rows[2].Result != "Win" || rows[2].ActualValue != 9 || rows[2].ProfitLoss != 1m)
        throw new Exception("Alias-linked corners were not settled against official outcomes.");
    if (rows[13].Result != "Win") throw new Exception("UTC/local midnight match failed.");
    if (rows[6].ActualValue is not null || rows[5].ActualValue is not null)
        throw new Exception("Historical read leaked a future or temporally invalid actual value.");
    var storedId = await connection.QuerySingleAsync<long?>($"SELECT ApiFootballFixtureId FROM [{schema}].BotI2026ShadowEvaluations WHERE ShadowEvaluationId=2", transaction: transaction);
    if (storedId is not null) throw new Exception("Read query rewrote audit.");
    Console.WriteLine("PASS 17 official-id, alias, competition-id, ambiguity, time, availability and immutable-audit cases.");
    await VerifyScorecardsAsync(connection, transaction, root.FullName, schema, source, function);
}
finally
{
    await transaction.RollbackAsync();
}
var remains = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=@schema", new { schema });
if (remains != 0) throw new Exception("Isolated test schema was not rolled back.");
Console.WriteLine("PASS full rollback; no test fixtures or schema remain.");
return 0;

static async Task VerifyScorecardsAsync(SqlConnection connection, SqlTransaction transaction,
    string root, string schema, string migration, string function)
{
    string Isolate(string sql) => sql
        .Replace("dbo.BotI2026ShadowEvaluations", $"[{schema}].BotI2026ShadowEvaluations")
        .Replace("dbo.MatchHistory", $"[{schema}].MatchHistory")
        .Replace("dbo.TeamNameAlias", $"[{schema}].TeamNameAlias");
    var fixture = File.ReadAllText(Path.Combine(root, "tests/BotILabResolution.Tests/scorecard-fixture.sql"))
        .Replace("[LabITest]", $"[{schema}]");
    foreach (var batch in System.Text.RegularExpressions.Regex.Split(fixture, @"(?im)^\s*GO\s*$"))
        if (!string.IsNullOrWhiteSpace(batch))
            await connection.ExecuteAsync(batch, transaction: transaction, commandTimeout: 60);
    // Refresh the function's SELECT * metadata after adding scorecard columns.
    await connection.ExecuteAsync(function, transaction: transaction, commandTimeout: 60);

    var start = migration.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_GetBotI2026ShadowScorecards", StringComparison.Ordinal);
    var end = migration.IndexOf("\nGO", start, StringComparison.Ordinal);
    var scorecard = migration[start..end];
    var optimized = Isolate(scorecard.Replace("dbo.sp_GetBotI2026ShadowScorecards", $"[{schema}].sp_OptimizedScorecard"));
    await connection.ExecuteAsync(optimized, transaction: transaction, commandTimeout: 60);

    // The original semantics resolve every row through the lab function, then
    // rank first approvals. Keep this independent oracle for the optimized path.
    var aggregateStart = scorecard.IndexOf("    Segments AS", StringComparison.Ordinal);
    var baseline = $"""
        CREATE OR ALTER PROCEDURE [{schema}].sp_BaselineScorecard
            @AsOfUtc DATETIME2(3), @ConfigurationVersion NVARCHAR(80) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Windows TABLE(WindowDays INT NOT NULL PRIMARY KEY);
            INSERT INTO @Windows VALUES (7), (30), (90);
            ;WITH Lab AS
            (
                SELECT lab.*,
                    ApprovedSequence = CASE WHEN lab.Decision=N'Approved' THEN ROW_NUMBER() OVER
                    (PARTITION BY lab.FixtureIdentity, lab.ConfigurationVersion, lab.Decision
                     ORDER BY lab.PredictionTimestampUtc, lab.ShadowEvaluationId) END
                FROM [{schema}].fn_BotI2026ShadowLab(@AsOfUtc) AS lab
                WHERE lab.PredictionTimestampUtc > DATEADD(DAY,-90,@AsOfUtc)
                  AND (@ConfigurationVersion IS NULL OR lab.ConfigurationVersion=@ConfigurationVersion)
            ),
        """ + scorecard[aggregateStart..];
    await connection.ExecuteAsync(baseline, transaction: transaction, commandTimeout: 60);
    var scenarios = new[]
    {
        (Cutoff: new DateTime(2026,9,22,3,0,0), Version: (string?)null),
        (Cutoff: new DateTime(2026,9,22,3,0,0), Version: (string?)"v1"),
        (Cutoff: new DateTime(2026,9,22,3,0,0), Version: (string?)"v2"),
        (Cutoff: new DateTime(2026,9,20,14,30,0), Version: (string?)null),
        (Cutoff: new DateTime(2026,9,22,3,0,0), Version: (string?)"missing")
    };
    foreach (var scenario in scenarios)
    {
        var parameters = new { AsOfUtc=scenario.Cutoff, ConfigurationVersion=scenario.Version };
        var expected = (await connection.QueryAsync($"[{schema}].sp_BaselineScorecard", parameters,
            transaction, commandType: System.Data.CommandType.StoredProcedure, commandTimeout:60)).ToArray();
        var actual = (await connection.QueryAsync($"[{schema}].sp_OptimizedScorecard", parameters,
            transaction, commandType: System.Data.CommandType.StoredProcedure, commandTimeout:60)).ToArray();
        if (actual.Length != expected.Length) throw new Exception("Optimized scorecard changed returned segments.");
        for (var index=0; index<actual.Length; index++)
        {
            var baselineRow=(IDictionary<string,object>)expected[index];
            var optimizedRow=(IDictionary<string,object>)actual[index];
            foreach (var (column, value) in baselineRow)
            {
                var result=optimizedRow[column];
                var equal=value is double expectedNumber && result is double actualNumber
                    ? Math.Abs(expectedNumber-actualNumber)<1e-10 : Equals(value,result);
                if (!equal) throw new Exception($"Scorecard mismatch: version={scenario.Version}, row={index}, column={column}, expected={value}, actual={result}.");
            }
            long Count(string column) => Convert.ToInt64(optimizedRow[column]);
            if (Count("ApprovedFixtureVersions") != Count("Settled")+Count("FutureApproved")+Count("MissingOfficialLink")+Count("AwaitingOfficialResult")+Count("InvalidOutcomeTimestamp"))
                throw new Exception("Independent approval coverage does not partition completely.");
            if (Count("SettledFixtures") > Count("Settled")) throw new Exception("Unique fixture count exceeds settled observations.");
        }
    }
    Console.WriteLine("PASS optimized I scorecards preserve all columns across windows, versions, duplicate snapshots, timestamp ties and historical cutoffs.");
}

sealed class Outcome
{
    public long ShadowEvaluationId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public long? ResolvedApiFootballFixtureId { get; init; }
    public string SettlementState { get; init; } = "";
    public string? Result { get; init; }
    public int? ActualValue { get; init; }
    public decimal? ProfitLoss { get; init; }
}
