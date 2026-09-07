using System.Data;
using System.Diagnostics;
using System.Transactions;
using AutomatedCornersBot.Api;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class SelectionContractTests
{
    public static async Task RunAsync()
    {
        var options = new AutomatedBotOptions { SqlConnectionString = ReadConnectionString() };
        var connectionString = options.ResolveSqlConnectionString();
        var settings = new SqlConnectionStringBuilder(connectionString);
        Require(settings.Enlist && settings.Pooling,
            "The rollback integration test requires ambient enlistment and connection pooling.");

        IDictionary<string, object> row;
        string baseline;
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var parameters = (await connection.QueryAsync<string>("""
                SELECT name FROM sys.parameters
                WHERE object_id=OBJECT_ID(N'dbo.sp_UpsertAutomatedCornerBetSelection')
                ORDER BY parameter_id;
                """)).ToArray();
            var expected = new[]
            {
                "@BotGCandidateId", "@RunId", "@BotKey", "@AutomationVersion", "@Source", "@SourceMatchId",
                "@ApiFootballFixtureId", "@SourceUrl", "@MatchDate", "@League", "@StandardizedLeague",
                "@HomeTeam", "@AwayTeam", "@StandardizedHomeTeam", "@StandardizedAwayTeam", "@HomeTeamGender",
                "@AwayTeamGender", "@SourceMarketType", "@MarketType", "@LineValue", "@SelectedSide", "@Odds",
                "@Stake", "@FlatStake", "@ImpliedProbability", "@ModelProbability", "@ProbabilityEdge",
                "@ExpectedValue", "@KellyFraction", "@SelectionScore", "@PredictedTotalCorners", "@PredTotalDirect",
                "@PredHomeCorners", "@PredAwayCorners", "@PredTotalCombined", "@DistanceToLine", "@ConfidenceLevel",
                "@OverUnderConfidenceLevel", "@ModelConsensus", "@ContextTotalCorners", "@ContextDifference",
                "@RecommendedSide", "@DecisionReason", "@AutomatedCornerBetSelectionId", "@MergeAction"
            };
            var missing = expected.Except(parameters, StringComparer.OrdinalIgnoreCase).ToArray();
            Require(missing.Length == 0,
                $"Selection SQL contract is missing {string.Join(", ", missing)} ({parameters.Length} parameters deployed; {expected.Length} sent by the repository).");

            // Reuse a real, unambiguous productive row. No synthetic selection is inserted.
            row = (IDictionary<string, object>)(await connection.QueryFirstOrDefaultAsync("""
                SELECT TOP (1) selected.*
                FROM dbo.AutomatedCornerBetSelections AS selected
                WHERE selected.BotKey IN (N'A',N'B',N'C2026',N'D2026',N'E2026',N'F2026')
                  AND selected.ApiFootballFixtureId > 0
                  AND selected.MatchDate < DATEADD(DAY,-1,SYSUTCDATETIME())
                  AND selected.LogicalPickKey IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM dbo.AutomatedCornerBetSelections AS other
                    WHERE other.BotKey=selected.BotKey AND other.MarketType=selected.MarketType
                      AND other.SelectedSide=selected.SelectedSide AND other.LineValue=selected.LineValue
                      AND other.ApiFootballFixtureId=selected.ApiFootballFixtureId
                      AND other.AutomatedCornerBetSelectionId<>selected.AutomatedCornerBetSelectionId)
                ORDER BY selected.AutomatedCornerBetSelectionId DESC;
                """, commandTimeout: 30)
                ?? throw new InvalidOperationException("No existing unambiguous selection with an official fixture is available for the rollback test."));
            baseline = await ReadSelectionJsonAsync(connection, Get<long>(row, "AutomatedCornerBetSelectionId"));
        }

        var selectionId = Get<long>(row, "AutomatedCornerBetSelectionId");
        var fixtureId = Get<long>(row, "ApiFootballFixtureId");
        var model = BuildCommand(row);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new SqlAutomationRepository(Options.Create(options), null!,
            NullLogger<SqlAutomationRepository>.Instance, cache);

        // Each connection is closed before the next opens. SqlClient reuses its
        // enlisted connection; no second simultaneous connection or MSDTC is needed.
        using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew,
            new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(3) },
            TransactionScopeAsyncFlowOption.Enabled))
        {
            var stopwatch = Stopwatch.StartNew();
            var retry = await repository.UpsertSelectionAsync(model, CancellationToken.None);
            Require(retry.SelectionId == selectionId && retry.MergeAction.Equals("UPDATE", StringComparison.OrdinalIgnoreCase),
                "A real C# repository retry must reuse the existing selection.");
            RequireLocalTransaction();
            Console.WriteLine($"PASS real selection retry: {stopwatch.ElapsedMilliseconds} ms");

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync("""
                    UPDATE dbo.AutomatedCornerBetSelections WITH (ROWLOCK)
                    SET ApiFootballFixtureId=NULL,
                        LogicalPickKey=HASHBYTES('SHA2_256',CONCAT(N'rollback-enrichment-test:',AutomatedCornerBetSelectionId))
                    WHERE AutomatedCornerBetSelectionId=@SelectionId;
                    """, new { SelectionId = selectionId });
            }

            stopwatch.Restart();
            var enriched = await repository.UpsertSelectionAsync(model, CancellationToken.None);
            Require(enriched.SelectionId == selectionId, "Official fixture enrichment must preserve the same selection id.");
            RequireLocalTransaction();
            Console.WriteLine($"PASS real fixture enrichment: {stopwatch.ElapsedMilliseconds} ms");

            await using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var actualFixture = await connection.QuerySingleAsync<long>("""
                    SELECT ApiFootballFixtureId FROM dbo.AutomatedCornerBetSelections
                    WHERE AutomatedCornerBetSelectionId=@SelectionId;
                    """, new { SelectionId = selectionId });
                Require(actualFixture == fixtureId, "The deployed procedure must accept and persist @ApiFootballFixtureId.");
            }
            // Deliberately never call Complete(): all changes above are rolled back.
        }

        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var restored = await ReadSelectionJsonAsync(connection, selectionId);
            Require(restored == baseline, "Rollback must restore every existing selection field, including identity and timestamps.");
        }
        Console.WriteLine("PASS deployed selection SQL contract: all 45 repository parameters, idempotent retry, fixture enrichment, local transaction and complete rollback");
    }

    private static PersistSelectionCommand BuildCommand(IDictionary<string, object> row)
    {
        double Number(string name) => row.TryGetValue(name, out var value) && value is not null && value is not DBNull
            ? Convert.ToDouble(value) : 0;
        string? Text(string name) => row.TryGetValue(name, out var value) && value is not null && value is not DBNull
            ? Convert.ToString(value) : null;
        var context = Number("ContextTotalCorners");
        return new PersistSelectionCommand
        {
            RunId = Get<Guid>(row, "RunId"), BotKey = Get<string>(row, "BotKey"),
            AutomationVersion = Get<string>(row, "AutomationVersion"),
            Odds = new UpcomingOddsRecord
            {
                Source = Get<string>(row, "Source"), SourceMatchId = Text("SourceMatchId"),
                ApiFootballFixtureId = Get<long>(row, "ApiFootballFixtureId"), SourceUrl = Text("SourceUrl"),
                MatchDate = Get<DateTime>(row, "MatchDate"), League = Get<string>(row, "League"),
                StandardizedLeague = Text("StandardizedLeague"), HomeTeam = Get<string>(row, "HomeTeam"),
                AwayTeam = Get<string>(row, "AwayTeam"), StandardizedHomeTeam = Text("StandardizedHomeTeam"),
                StandardizedAwayTeam = Text("StandardizedAwayTeam"), HomeTeamGender = Text("HomeTeamGender") ?? "M",
                AwayTeamGender = Text("AwayTeamGender") ?? "M", MarketType = Get<string>(row, "SourceMarketType"),
                LineValue = Get<decimal>(row, "LineValue")
            },
            SelectedSide = Get<string>(row, "SelectedSide"), SelectedOdds = Get<decimal>(row, "Odds"),
            Stake = Get<decimal>(row, "Stake"), ImpliedProbability = Number("ImpliedProbability"),
            ModelProbability = Number("ModelProbability"), ProbabilityEdge = Number("ProbabilityEdge"),
            ExpectedValue = Number("ExpectedValue"), KellyFraction = Number("KellyFraction"), SelectionScore = Number("SelectionScore"),
            CornersPrediction = new PredictionResultDto
            {
                PredictedTotalCorners = Number("PredictedTotalCorners"), PredTotalDirect = Number("PredTotalDirect"),
                PredHomeCorners = Number("PredHomeCorners"), PredAwayCorners = Number("PredAwayCorners"),
                PredTotalCombined = Number("PredTotalCombined"), DistanceToLine = Number("DistanceToLine"),
                Confidence = Text("ConfidenceLevel"), ModelConsensus = Text("ModelConsensus"), RecommendedSide = Text("RecommendedSide")
            },
            PredictionContext = new PredictionContextDto(new PredictionComparisonDto(context, null, "", context, context,
                context, context, context, context, context, context, context), [], [], [], []),
            DecisionReason = Text("DecisionReason") ?? ""
        };
    }

    private static Task<string> ReadSelectionJsonAsync(SqlConnection connection, long selectionId) =>
        connection.QuerySingleAsync<string>("""
            SELECT (SELECT * FROM dbo.AutomatedCornerBetSelections
                WHERE AutomatedCornerBetSelectionId=@SelectionId
                FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);
            """, new { SelectionId = selectionId });

    private static T Get<T>(IDictionary<string, object> row, string key) => row[key] is T value
        ? value : (T)Convert.ChangeType(row[key], typeof(T));

    private static void RequireLocalTransaction() => Require(
        Transaction.Current?.TransactionInformation.DistributedIdentifier == Guid.Empty,
        "The test must remain on a local SQL transaction; distributed enlistment is not allowed.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string ReadConnectionString()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".env"))) directory = directory.Parent;
        Require(directory is not null, "Could not locate the repository environment for the SQL integration test.");
        return File.ReadLines(Path.Combine(directory!.FullName, ".env"))
            .First(line => line.StartsWith("AZURE_SQL_CONNECTION_STRING=", StringComparison.Ordinal))
            .Split('=', 2)[1].Trim().Trim('"', '\'');
    }
}
