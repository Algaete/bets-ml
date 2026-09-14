using System.Reflection;
using System.Text.Json;
using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation.BotC;
using Dapper;
using Microsoft.Data.SqlClient;

internal static class CalibrationWriteTests
{
    public static async Task RunAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, ".env"))) root = root.Parent;
        var line = File.ReadLines(Path.Combine(root!.FullName, ".env"))
            .First(value => value.StartsWith("AZURE_SQL_CONNECTION_STRING="));
        await using var connection = new SqlConnection(line.Split('=', 2)[1].Trim().Trim('"', '\''));
        await connection.OpenAsync();
        await RunAsync(connection);
    }

    public static async Task RunAsync(SqlConnection connection)
    {
        var runId = Guid.NewGuid();
        var insertedIds = new List<long>();
        var oddsId = await connection.ExecuteScalarAsync<long>("SELECT MIN(PartidoProximoCuotaId) FROM dbo.PartidosProximosCuotas;");
        var now = DateTime.UtcNow;
        var empty = new BotCPickDecisionEngine().Evaluate(new BotCPickEvaluationInput(
            "AwayTeamGoals", 1.5m, 1.8m, 2m, now, now.AddDays(1), 1, 1, "test", "test", [], [], [], []),
            new BotCStrategyConfiguration());
        var build = typeof(SqlAutomationRepository).GetMethod("BuildBotCEvaluationCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cases = new (string Json, double BaseProbability, double? Probability)[]
        {
            ("{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.71}}", .61, .71),
            ("{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":\"0.71\"}}", .61, null),
            ("{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":null}}", .61, null),
            ("{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.71},\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.63}}", .61, .63),
            ("{}", .61, .61), ("invalid JSON", .61, .61), ("{\"legacy\":true}", .61234567, .612346)
        };
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            try
            {
                foreach (var (json, baseProbability, expected) in cases)
                {
                    var decision = empty with
                    {
                        Decision = "Rejected", SelectedSide = "Under", SelectedOdds = 2m,
                        BaseCalibratedProbability = baseProbability, MarketNoVigProbability = .5, DataQualityScore = .8,
                        FeatureSnapshotJson = json, Summary = "Rollback calibration test"
                    };
                    var model = new PersistBotCEvaluationCommand(runId, "C2026", "calibration-test",
                        new UpcomingOddsRecord
                        {
                            PartidoProximoCuotaId = oddsId, Source = "Pinnacle", MatchDate = now.AddYears(2),
                            League = "Rollback test", HomeTeam = "Home", AwayTeam = "Away",
                            MarketType = "GoalsAwayTeam", LineValue = 1.5m, OverOdds = 1.8m, UnderOdds = 2m,
                            UpdatedAtUtc = now
                        }, "AwayTeamGoals", "test", runId.ToString(), decision, now.AddDays(-1));
                    using var command = (SqlCommand)build.Invoke(null, [model])!;
                    command.Connection = connection;
                    command.Transaction = transaction;
                    Console.WriteLine($"Checking write-time calibration case {insertedIds.Count + 1}");
                    await command.ExecuteNonQueryAsync();
                    var id = await connection.ExecuteScalarAsync<long>(
                        "SELECT AutomatedBotPickEvaluationId FROM dbo.AutomatedBotPickEvaluations WHERE IdempotencyKey=@Key;",
                        new { Key = command.Parameters["@IdempotencyKey"].Value }, transaction);
                    var probability = await connection.QuerySingleAsync<double?>(
                        "SELECT SourceProbability FROM dbo.AutomatedBotCalibrationProbabilityCache WHERE EvaluationId=@Id;", new { Id = id }, transaction);
                    insertedIds.Add(id);
                    Require(probability == expected, $"Write-time probability differs from canonical resolver: {JsonSerializer.Serialize(probability)}.");
                    Require(await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dbo.AutomatedBotCalibrationEvaluationCache WHERE AutomatedBotPickEvaluationId=@Id AND MatchDate>SYSUTCDATETIME();",
                        new { Id = id }, transaction) == 1, "Future evaluations must already have compact metadata.");

                    await command.ExecuteNonQueryAsync();
                    Require(await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dbo.AutomatedBotCalibrationProbabilityCache WHERE EvaluationId=@Id;", new { Id = id }, transaction) == 1,
                        "Idempotent retries must restore exactly one probability cache row.");
                    await connection.ExecuteAsync("UPDATE dbo.AutomatedBotPickEvaluations SET ApiFootballFixtureId=999999999 WHERE AutomatedBotPickEvaluationId=@Id;",
                        new { Id = id }, transaction);
                    Require(await connection.ExecuteScalarAsync<long>(
                        "SELECT ApiFootballFixtureId FROM dbo.AutomatedBotCalibrationEvaluationCache WHERE AutomatedBotPickEvaluationId=@Id;", new { Id = id }, transaction) == 999999999,
                        "Fixture corrections must update compact metadata immediately.");
                    Require(await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dbo.AutomatedBotCalibrationProbabilityCache WHERE EvaluationId=@Id;", new { Id = id }, transaction) == 0,
                        "External evaluation corrections must invalidate the probability cache.");
                    await connection.ExecuteAsync("DELETE dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId=@Id;", new { Id = id }, transaction);
                    Require(await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM dbo.AutomatedBotCalibrationEvaluationCache WHERE AutomatedBotPickEvaluationId=@Id;", new { Id = id }, transaction) == 0,
                        "Deleted evaluations cannot remain in compact metadata.");
                }
                Require(await connection.ExecuteScalarAsync<int>("SELECT @@TRANCOUNT;", transaction: transaction) == 1,
                    "The procedure must preserve its caller's rollback transaction.");
                await transaction.RollbackAsync();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Calibration write failure before transaction cleanup: {exception}");
                throw;
            }
        }
        Require(await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.AutomatedBotPickEvaluations WHERE AutomatedBotPickEvaluationId IN @Ids;", new { Ids = insertedIds }) == 0,
            "Calibration integration tests must not leave audit records behind.");
        Console.WriteLine("PASS write-time calibration: numeric/null/string/duplicate/legacy JSON, idempotent retries, corrections and deletion; all writes rolled back");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
