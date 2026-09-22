using Microsoft.Data.SqlClient;

internal static class ScorecardSqlContractTests
{
    // Optional read-only SQL contract test: executes the production expression
    // against VALUES, never against application rows or permanent tables.
    public static void Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("BOT_G_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set BOT_G_SQL_CONNECTION_STRING to run --scorecard-sql.");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CornersPrediction.sln")))
            directory = directory.Parent;
        var sql = File.ReadAllText(Path.Combine(directory!.FullName, "CornersPredictionApi", "sql", "20260819_bot_g2026.sql"));
        var procedure = sql[sql.IndexOf("CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026Scorecard", StringComparison.Ordinal)..];
        var start = procedure.IndexOf("        CASE\n", StringComparison.Ordinal);
        var end = procedure.IndexOf("        END AS OutcomeScore,", start, StringComparison.Ordinal) + "        END".Length;
        var expression = procedure[start..end];
        var query = $$"""
            SELECT evaluation.Id, evaluation.Expected, scored.OutcomeScore
            FROM (VALUES
              (1, 0.5, 0.8, 0.7, 0.6, N'Win', 1.0),
              (2, 1.5, 0.8, 0.7, 0.6, N'Loss', 0.0),
              (3, 0.5, 0.0, 0.6, 0.6, N'Win', NULL),
              (4, 1.0, 0.8, 0.7, 0.6, N'Push', NULL),
              (5, 0.75, 0.8, 0.7, 0.6, N'HalfWin', NULL),
              (6, 1.5, 0.8, 0.7, NULL, N'Win', NULL),
              (7, 1.5, 0.8, NULL, 0.6, N'Loss', NULL),
              (8, 1.5, 0.8, 1.2, 0.6, N'Win', NULL),
              (9, 0.5, 0.8, 0.7, 0.6, N'Void', NULL),
              (10, 0.5, 0.8, 0.7, 0.6, N'Pending', NULL),
              (11, 0.25, 0.8, 0.7, 0.6, N'Loss', NULL)
            ) evaluation(Id, LineValue, CalibrationReliability, CalibratedProbability, MarketNoVigProbability, Result, Expected)
            CROSS APPLY (SELECT {{expression}} AS OutcomeScore) scored ORDER BY Id;
            """;
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(query, connection) { CommandTimeout = 30 };
        using var reader = command.ExecuteReader();
        var checkedRows = 0;
        while (reader.Read())
        {
            if (!Equals(reader.GetValue(1), reader.GetValue(2)))
                throw new InvalidOperationException($"G scorecard outcome test {reader.GetInt32(0)} failed.");
            checkedRows++;
        }
        if (checkedRows != 11)
            throw new InvalidOperationException("G scorecard SQL contract did not return all test cases.");
        Console.WriteLine("PASS SQL G scorecard: paired binary outcomes, missing model, Asian lines, null probabilities, void and pending (11 cases; read-only)");
    }
}
