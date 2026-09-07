using System.Reflection;
using System.Text.Json;
using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotE;
using CornersPredictionApi.RecommendationJobs;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var repository = new LeaseRepository();
var worker = new RecommendationJobWorker(null!, Options.Create(new RecommendationJobOptions()),
    NullLogger<RecommendationJobWorker>.Instance, new TestEnvironment());
var recover = typeof(RecommendationJobWorker).GetMethod("RecoverLocalLeasesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
await (Task)recover.Invoke(worker, [repository, CancellationToken.None])!;
Check(repository.Released.SequenceEqual(new[] { repository.DeadJob }), "Only the exited local process can release its reservation.");
Check(new RecommendationJobOptions().LeaseMinutes == 5, "Crash recovery must not wait an hour.");
Console.WriteLine("PASS local recovery preserves live owners and releases only exited processes");

if (args.Contains("--sql")) await CheckCalibrationSql();
if (args.Contains("--selection-sql")) await SelectionContractTests.RunAsync();
return 0;

static async Task CheckCalibrationSql()
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, ".env"))) root = root.Parent;
    var environment = File.ReadLines(Path.Combine(root!.FullName, ".env"))
        .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"', '\''));
    await using var connection = new SqlConnection(environment["AZURE_SQL_CONNECTION_STRING"]);
    await connection.OpenAsync();
    await connection.ExecuteAsync("""
        CREATE TABLE #Evaluations (
          AutomatedBotPickEvaluationId BIGINT PRIMARY KEY, ApiFootballFixtureId BIGINT NULL,
          PublishedSelectionId BIGINT NULL, MatchDate DATETIME2, HomeTeam NVARCHAR(150), AwayTeam NVARCHAR(150),
          MarketType NVARCHAR(50), SelectedSide NVARCHAR(10), LineValue DECIMAL(6,2), SelectedOdds DECIMAL(10,2),
          BaseCalibratedProbability DECIMAL(9,6), MarketNoVigProbability DECIMAL(9,6), DataQualityScore DECIMAL(9,6),
          FeatureSnapshotJson NVARCHAR(MAX), BaseModelTrainedThroughUtc DATETIME2, BaseModelVersion NVARCHAR(120),
          BotKey NVARCHAR(50), Decision NVARCHAR(20),
          CalibrationSourceJson AS JSON_QUERY(CASE WHEN ISJSON(FeatureSnapshotJson)=1 THEN FeatureSnapshotJson ELSE N'{}' END,N'$.empiricalCalibration') PERSISTED);
        CREATE TABLE #History (
          Id BIGINT PRIMARY KEY, ApiFootballFixtureId BIGINT NULL, MatchDate DATE,
          StandardizedHomeTeam NVARCHAR(150), StandardizedAwayTeam NVARCHAR(150), HomeTeam NVARCHAR(150), AwayTeam NVARCHAR(150),
          HomeGoals INT NULL, AwayGoals INT NULL, HomeCorners INT NULL, AwayCorners INT NULL,
          HomeShots INT NULL, AwayShots INT NULL, HomeShotsOnGoal INT NULL, AwayShotsOnGoal INT NULL,
          FixtureStatus NVARCHAR(20), ApiFootballGoalsAvailable BIT, ApiFootballCornersAvailable BIT,
          ApiFootballShotsAvailable BIT, ApiFootballShotsOnGoalAvailable BIT);
        CREATE TABLE #Selections (AutomatedCornerBetSelectionId BIGINT PRIMARY KEY, MatchHistoryId BIGINT NULL);
        CREATE TABLE #ProbabilityCache (EvaluationId BIGINT PRIMARY KEY, SourceHash BINARY(32), SourceProbability FLOAT NULL);
        INSERT #ProbabilityCache VALUES (3,0x00,0.71),(4,0x00,NULL);
        INSERT #History VALUES
          (1,100,'2026-09-01',NULL,NULL,N'Águila',N'Visitante',2,1,5,3,12,8,6,4,'FT',1,1,1,1),
          (2,200,'2026-09-01',NULL,NULL,N'Otro',N'Club',4,2,8,2,18,10,7,5,'FT',1,1,1,1),
          (3,300,'2026-09-01',NULL,NULL,N'Ambiguo',N'Club',0,0,1,1,3,3,1,1,'FT',1,1,1,1),
          (4,300,'2026-09-01',NULL,NULL,N'Ambiguo',N'Club',1,1,2,2,4,4,2,2,'FT',1,1,1,1),
          (5,500,'2026-09-01',NULL,NULL,N'Pendiente',N'Club',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'NS',0,0,0,0);
        INSERT #Selections VALUES (10,2);
        """);
    var markets = new[] { "TotalGoals", "HomeTeamGoals", "AwayTeamGoals", "TotalCorners", "HomeTeamCorners", "AwayTeamCorners",
        "TotalShots", "HomeTeamShots", "AwayTeamShots", "TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal" };
    var snapshots = new[] { "{}", "invalid", "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.71}}",
        "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":\"0.71\"}}",
        "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":null}}",
        "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.4,\"probabilityBeforeEmpiricalCalibration\":0.8}}",
        "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.4},\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":0.8}}" };
    long id = 0;
    foreach (var market in markets)
    foreach (var json in snapshots)
    {
        id++;
        await Insert(id, market, json, id % 2 == 0 ? 100 : null, null, "Aguila");
    }
    var publishedPriorityId = ++id;
    await Insert(id, "TotalGoals", "{}", 100, 10, "Aguila");
    var ambiguousId = ++id;
    await Insert(id, "TotalGoals", "{}", 300, null, "Ambiguo");
    var unavailableId = ++id;
    await Insert(id, "TotalGoals", "{}", 500, null, "Pendiente");
    var futureModelId = ++id;
    await Insert(id, "TotalGoals", "{}", 100, null, "Aguila");
    await connection.ExecuteAsync("UPDATE #Evaluations SET BaseModelTrainedThroughUtc='2026-09-02' WHERE AutomatedBotPickEvaluationId=@Id", new { Id = futureModelId });

    var baseline = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CalibrationReference.sql"));
    var optimized = (string)typeof(SqlAutomationRepository).GetField("CalibrationHistorySql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
    static string Localize(string sql) => sql.Replace("dbo.AutomatedBotPickEvaluations", "#Evaluations")
        .Replace("dbo.AutomatedCornerBetSelections", "#Selections").Replace("dbo.MatchHistory", "#History")
        .Replace("dbo.AutomatedBotCalibrationProbabilityCache", "#ProbabilityCache");
    var parameters = new { SourceBotKey = "C2026", AsOfDateUtc = new DateTime(2026,9,3) };
    var originalRows = (await connection.QueryAsync<CalibrationRow>(Localize(baseline), parameters)).ToArray();
    var optimizedRows = (await connection.QueryAsync<CalibrationRow>(Localize(optimized), parameters)).ToArray();
    string Normalize(CalibrationRow row) => JsonSerializer.Serialize(new {
        row.EvaluationId, row.FixtureId, row.MatchDateUtc, row.MarketType, row.SelectedSide, row.LineValue, row.Odds,
        row.ActualValue, Probability = row.HasCachedProbability ? row.CachedProbability
            : BotECalibrationSourceProbabilityResolver.Resolve(row.FeatureSnapshotJson, (double?)row.BaseCalibratedProbability),
        row.MarketNoVigProbability, row.DataQualityScore, row.BaseModelVersion });
    Check(originalRows.Select(Normalize).SequenceEqual(optimizedRows.Select(Normalize)), "Calibration identities, order, probability types and outcomes must remain identical.");
    Check(optimizedRows.Single(row => row.EvaluationId == publishedPriorityId).FixtureId == 200, "Published match identity must retain priority.");
    Check(!optimizedRows.Any(row => new[] { ambiguousId, unavailableId, futureModelId }.Contains(row.EvaluationId)), "Ambiguous matches, missing official outcomes and temporal leakage remain excluded.");
    Console.WriteLine($"PASS calibration SQL: {optimizedRows.Length} identical rows across 12 markets, numeric/string/null/malformed snapshots, identity ambiguity and model cutoffs");

    string SqlPart(string name) => Localize((string)typeof(SqlAutomationRepository)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!);
    await connection.ExecuteAsync("DECLARE @SourceBotKey NVARCHAR(50)=N'C2026',@AsOfDateUtc DATETIME2='2026-09-03';\n" + SqlPart("CalibrationPreparationSql"));
    var preparedBatches = 0;
    while (true)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var batchRows = (await connection.QueryAsync<CalibrationRow>(
            "DECLARE @SnapshotBatchSize INT=10;\n" + SqlPart("CalibrationSnapshotBatchSql")
            + "\nSELECT * FROM #CalibrationMissingSnapshots;", transaction: transaction)).ToArray();
        if (batchRows.Length > 0)
        {
            foreach (var row in batchRows)
                await connection.ExecuteAsync("INSERT #CalibrationCachedSources VALUES (@EvaluationId,@Probability);",
                    new { row.EvaluationId, Probability=BotECalibrationSourceProbabilityResolver.Resolve(row.FeatureSnapshotJson,(double?)row.BaseCalibratedProbability) }, transaction);
            await connection.ExecuteAsync("DELETE missing FROM #CalibrationMissingSources missing INNER JOIN #CalibrationMissingSnapshots prepared ON prepared.EvaluationId=missing.EvaluationId;", transaction:transaction);
            preparedBatches++;
        }
        await transaction.CommitAsync();
        if (batchRows.Length == 0) break;
    }
    var stagedRows = (await connection.QueryAsync<CalibrationRow>(SqlPart("CalibrationResultsSql"))).ToArray();
    Check(preparedBatches > 1 && originalRows.Select(Normalize).SequenceEqual(stagedRows.Select(Normalize)),
        "Incremental transactions must preserve all observations and temp tables between batches.");
    Check(stagedRows.All(row=>row.HasCachedProbability),"Completed preparation must use resolved cache values, including null probabilities.");
    Console.WriteLine($"PASS incremental calibration: {preparedBatches} committed blocks preserve every result and connection-scoped temporary table");

    await connection.ExecuteAsync("CREATE TABLE #BatchCalls (Ordinal INT IDENTITY PRIMARY KEY, BotKey NVARCHAR(50), Odds DECIMAL(9,4), Snapshot NVARCHAR(MAX));");
    await connection.ExecuteAsync("CREATE PROCEDURE #CaptureEvaluation @BotKey NVARCHAR(50), @Odds DECIMAL(9,4), @Snapshot NVARCHAR(MAX) AS INSERT #BatchCalls(BotKey,Odds,Snapshot) VALUES (@BotKey,@Odds,@Snapshot);");
    var commands = Enumerable.Range(0,20).Select(index =>
    {
        var cmd = new SqlCommand("#CaptureEvaluation") { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add(new SqlParameter("@BotKey",System.Data.SqlDbType.NVarChar,50) { Value=$"Bot '{index}" });
        cmd.Parameters.Add(new SqlParameter("@Odds",System.Data.SqlDbType.Decimal) { Precision=9,Scale=4,Value=2.1345m });
        cmd.Parameters.Add(new SqlParameter("@Snapshot",System.Data.SqlDbType.NVarChar,-1) { Value="{\"reason\":\"literal '; SELECT text\"}" });
        return cmd;
    }).ToArray();
    using var batch = (SqlCommand)typeof(SqlAutomationRepository).GetMethod("BuildEvaluationBatch",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[connection,commands])!;
    await batch.ExecuteNonQueryAsync();
    var captured = (await connection.QueryAsync<(int Ordinal,string BotKey,decimal Odds,string Snapshot)>("SELECT Ordinal,BotKey,Odds,Snapshot FROM #BatchCalls ORDER BY Ordinal")).ToArray();
    Check(captured.Length==20 && captured.Select(row=>row.BotKey).SequenceEqual(Enumerable.Range(0,20).Select(index=>$"Bot '{index}")),"Batch must preserve every call in original order.");
    Check(captured.All(row=>row.Odds==2.1345m && row.Snapshot=="{\"reason\":\"literal '; SELECT text\"}"),"Precision and JSON must remain parameterized and unchanged.");
    Check(commands.All(cmd=>cmd.Parameters[0].ParameterName=="@BotKey"),"Batch parameter renaming must not mutate source commands.");
    foreach(var cmd in commands) cmd.Dispose();
    Console.WriteLine("PASS 20 ordered stored-procedure calls in one round trip, with unchanged decimals and literal JSON parameters");

    Task Insert(long evalId, string market, string json, long? fixture, long? selection, string home) => connection.ExecuteAsync("""
        INSERT #Evaluations (AutomatedBotPickEvaluationId,ApiFootballFixtureId,PublishedSelectionId,MatchDate,HomeTeam,AwayTeam,
            MarketType,SelectedSide,LineValue,SelectedOdds,BaseCalibratedProbability,MarketNoVigProbability,DataQualityScore,
            FeatureSnapshotJson,BaseModelTrainedThroughUtc,BaseModelVersion,BotKey,Decision)
        VALUES (@Id,@Fixture,@Selection,'2026-09-01T15:00:00',@Home,N'Visitante',@Market,N'Over',2.5,1.9,0.55,0.52,0.9,@Json,'2026-08-01',N'v1',N'C2026',N'Approved');
        """, new { Id=evalId, Fixture=fixture, Selection=selection, Home=home, Market=market, Json=json });
}

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed class CalibrationRow
{
    public long EvaluationId { get; init; } public long FixtureId { get; init; } public DateTime MatchDateUtc { get; init; }
    public string MarketType { get; init; } = ""; public string SelectedSide { get; init; } = "";
    public decimal LineValue { get; init; } public decimal Odds { get; init; } public int ActualValue { get; init; }
    public decimal? BaseCalibratedProbability { get; init; } public decimal MarketNoVigProbability { get; init; }
    public decimal DataQualityScore { get; init; } public string? FeatureSnapshotJson { get; init; } public string? BaseModelVersion { get; init; }
    public bool HasCachedProbability { get; init; } public double? CachedProbability { get; init; }
}
sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development"; public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = "/tmp"; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class LeaseRepository : IRecommendationJobRepository
{
    public Guid DeadJob { get; } = Guid.NewGuid(); public List<Guid> Released { get; } = [];
    public Task<IReadOnlyList<RecommendationJobLease>> GetLocalLeasesAsync(string hostName, CancellationToken ct) => Task.FromResult<IReadOnlyList<RecommendationJobLease>>([
        new(DeadJob, $"{hostName}:{int.MaxValue}:dead"), new(Guid.NewGuid(), $"{hostName}:{Environment.ProcessId}:alive"),
        new(Guid.NewGuid(), $"{hostName}:invalid:unknown")]);
    public Task ReleaseAsync(Guid id, string owner, CancellationToken ct) { Released.Add(id); return Task.CompletedTask; }
    public Task<RecommendationJobDto> EnqueueAsync(CreateRecommendationJobCommand c,string h,CancellationToken ct)=>throw new NotSupportedException();
    public Task<RecommendationJobDto?> GetAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    public Task<IReadOnlyList<RecommendationJobDto>> ListAsync(int take,CancellationToken ct)=>throw new NotSupportedException();
    public Task<bool> CancelAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
    public Task<RecommendationJobDto?> TryClaimNextAsync(string owner,TimeSpan lease,CancellationToken ct)=>throw new NotSupportedException();
    public Task<bool> RenewLeaseAsync(Guid id,string owner,TimeSpan lease,CancellationToken ct)=>throw new NotSupportedException();
    public Task<RecommendationJobDto?> CompleteBatchAsync(Guid id,string owner,RecommendationJobBatchProgress progress,CancellationToken ct)=>throw new NotSupportedException();
    public Task<RecommendationJobDto?> RecordFailureAsync(Guid id,string owner,string error,CancellationToken ct)=>throw new NotSupportedException();
}
