using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

internal static class JobErrorSqlTests
{
    public static async Task RunAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, ".env"))) root = root.Parent;
        Require(root is not null, "Could not locate the repository for the SQL integration test.");
        var connectionString = File.ReadLines(Path.Combine(root!.FullName, ".env"))
            .First(line => line.StartsWith("AZURE_SQL_CONNECTION_STRING=", StringComparison.Ordinal))
            .Split('=', 2)[1].Trim().Trim('"', '\'');
        var migration = await File.ReadAllTextAsync(Path.Combine(root.FullName,
            "CornersPredictionApi/sql/20260906_recommendation_job_errors.sql"));
        var isolatedProcedure = Regex.Replace(migration, @"^\s*GO\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Replace("dbo.sp_CompleteAutomatedRecommendationJobBatch", "#CompleteJob")
            .Replace("dbo.AutomatedRecommendationJobs", "#Jobs")
            .Replace("dbo.vw_AutomatedRecommendationJobs", "#Jobs")
            // Temporary procedures use tempdb's literal collation while this
            // database supplies the parameter collation. Production has one
            // collation for both; explicitly align the temporary NULLIF operand.
            .Replace("NULLIF(@ErrorSummary,N'')", "NULLIF(@ErrorSummary COLLATE DATABASE_DEFAULT,N'')");
        Require(!isolatedProcedure.Contains("dbo.", StringComparison.OrdinalIgnoreCase),
            "The integration test must target only its session-local temporary procedure and table.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            CREATE TABLE #Jobs (
                RecommendationJobId UNIQUEIDENTIFIER PRIMARY KEY,
                Status NVARCHAR(20) COLLATE DATABASE_DEFAULT NOT NULL DEFAULT N'Running',
                NextBatchNumber INT NOT NULL DEFAULT 1,
                TotalBatches INT NULL,
                ProcessedBatches INT NOT NULL DEFAULT 0,
                SelectedMatches INT NOT NULL DEFAULT 0,
                InsertedRows INT NOT NULL DEFAULT 0,
                UpdatedRows INT NOT NULL DEFAULT 0,
                SkippedMatches INT NOT NULL DEFAULT 0,
                ErrorMatches INT NOT NULL DEFAULT 0,
                AttemptCount INT NOT NULL DEFAULT 0,
                LastRunId UNIQUEIDENTIFIER NULL,
                LastError NVARCHAR(2000) COLLATE DATABASE_DEFAULT NULL,
                LeaseOwner NVARCHAR(150) COLLATE DATABASE_DEFAULT NULL,
                LeaseExpiresAtUtc DATETIME2 NULL,
                NextAttemptAtUtc DATETIME2 NULL,
                UpdatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                CompletedAtUtc DATETIME2 NULL
            );
            """, commandTimeout: 30);
        try
        {
            await connection.ExecuteAsync("EXEC(@ProcedureDefinition);",
                new { ProcedureDefinition = isolatedProcedure }, commandTimeout: 30);
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException($"Temporary migration setup failed at SQL line {exception.LineNumber}, procedure '{exception.Procedure}'.", exception);
        }

        var erroredJob = Guid.NewGuid();
        const string owner = "isolated-job-test";
        const string diagnostic = "Local vs Visita: SQL contract mismatch";
        await Seed(erroredJob);
        var first = await Complete(erroredJob, owner, batch: 1, totalBatches: 2, errors: 1, diagnostic);
        Require(first.Status == "Queued" && first.NextBatchNumber == 2 && first.ProcessedBatches == 1,
            "The first batch must advance to a queued second batch.");
        Require(first.ErrorMatches == 1 && first.LastError == diagnostic,
            "The failed match count and actionable diagnostic must both be saved.");
        Require(first.LeaseOwner is null && first.CompletedAtUtc is null,
            "An unfinished batch must release its lease without marking the job completed.");
        await connection.ExecuteAsync("UPDATE #Jobs SET Status=N'Running', LeaseOwner=@Owner WHERE RecommendationJobId=@Id;",
            new { Owner = owner, Id = erroredJob });
        var beforeWrongOwner = await Snapshot(erroredJob);
        await Complete(erroredJob, "wrong-worker", batch: 2, totalBatches: 2, errors: 99, "Must not persist");
        Require(await Snapshot(erroredJob) == beforeWrongOwner,
            "A worker that does not own the lease must not modify any job field.");
        var completed = await Complete(erroredJob, owner, batch: 2, totalBatches: 2, errors: 0, summary: null);
        Require(completed.Status == "Completed" && completed.ProcessedBatches == 2 && completed.NextBatchNumber == 3,
            "The clean final batch must complete the same job.");
        Require(completed.ErrorMatches == 1 && completed.LastError == diagnostic,
            "A clean later batch must preserve the earlier error count and diagnostic.");
        Require(completed.CompletedAtUtc is not null && completed.LeaseOwner is null,
            "A completed job must have its completion timestamp and no active lease.");
        var completedSnapshot = await Snapshot(erroredJob);
        await Complete(erroredJob, owner, batch: 2, totalBatches: 2, errors: 1, "Duplicate completion");
        Require(await Snapshot(erroredJob) == completedSnapshot,
            "Repeating completion after the lease is released must not double-count batches or errors.");

        var cleanJob = Guid.NewGuid();
        await Seed(cleanJob);
        await connection.ExecuteAsync("UPDATE #Jobs SET LastError=N'An earlier retry failed', AttemptCount=1 WHERE RecommendationJobId=@Id;",
            new { Id = cleanJob });
        var clean = await Complete(cleanJob, owner, batch: 1, totalBatches: 1, errors: 0, summary: null);
        Require(clean.Status == "Completed" && clean.ErrorMatches == 0 && clean.LastError is null && clean.AttemptCount == 0,
            "A successful job with no failed matches must clear an obsolete retry error.");

        var fallbackJob = Guid.NewGuid();
        await Seed(fallbackJob);
        var fallback = await Complete(fallbackJob, owner, batch: 1, totalBatches: 1, errors: 2, summary: null);
        Require(fallback.ErrorMatches == 2 && fallback.LastError == "El lote 1 terminó con 2 errores.",
            "A batch without detailed diagnostics must still store an explanatory error count.");
        Console.WriteLine("PASS isolated real job migration: first-batch diagnostic survives clean completion, wrong lease and duplicate completion cannot write, clean jobs clear old retry errors, missing summaries receive a fallback");

        Task Seed(Guid id) => connection.ExecuteAsync(
            "INSERT #Jobs (RecommendationJobId,LeaseOwner,LeaseExpiresAtUtc) VALUES (@Id,@Owner,DATEADD(MINUTE,5,SYSUTCDATETIME()));",
            new { Id = id, Owner = owner });
        Task<string> Snapshot(Guid id) => connection.QuerySingleAsync<string>(
            "SELECT (SELECT * FROM #Jobs WHERE RecommendationJobId=@Id FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER);",
            new { Id = id });
        Task<JobState> Complete(Guid id, string worker, int batch, int totalBatches, int errors, string? summary) =>
            connection.QuerySingleAsync<JobState>("""
                EXEC #CompleteJob @RecommendationJobId=@Id, @WorkerId=@Worker,
                    @CompletedBatchNumber=@Batch, @TotalBatches=@Total,
                    @RunId=@RunId, @SelectedMatches=0, @InsertedRows=0, @UpdatedRows=0,
                    @SkippedMatches=0, @ErrorMatches=@Errors, @ErrorSummary=@Summary;
                """, new { Id = id, Worker = worker, Batch = batch, Total = totalBatches,
                    RunId = Guid.NewGuid(), Errors = errors, Summary = summary }, commandTimeout: 30);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class JobState
    {
        public string Status { get; init; } = "";
        public int NextBatchNumber { get; init; }
        public int ProcessedBatches { get; init; }
        public int ErrorMatches { get; init; }
        public int AttemptCount { get; init; }
        public string? LastError { get; init; }
        public string? LeaseOwner { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }
}
