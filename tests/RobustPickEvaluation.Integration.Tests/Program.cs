using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CornersPrediction.Application.RobustPickEvaluation;
using CornersPrediction.Domain.RobustPickEvaluation;
using CornersPredictionApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.TransactSql.ScriptDom;

var tests = new (string Name, Func<Task> Execute)[]
{
    ("migration is non-destructive and append-only", MigrationIsAppendOnly),
    ("migration parses as SQL Server syntax", MigrationParsesWithScriptDom),
    ("SQL residual history is temporal cancellable and family isolated", RepositoryResidualQueryIsTemporal),
    ("SQL backfill preview uses bounded sargable date ranges", RepositoryBackfillPreviewIsSargable),
    ("fake repository sequences idempotent superseding snapshots", FakeRepositoryAppendOnly),
    ("evaluation service isolates Shadow and enforces rejection", EvaluationModesAreEffective),
    ("scenario side stability is measured against the selected line", ScenarioSideUsesMarketLine),
    ("optional scenario provider failure is isolated", OptionalScenarioProviderFailureIsIsolated),
    ("backfill dry-run previews without evaluating or appending", BackfillDryRunDoesNotWrite),
    ("backfill preserves original evidence and is idempotent", BackfillPreservesEvidence),
    ("backfill stops with a resumable checkpoint", BackfillStopsAtFailureCheckpoint),
    ("robust API exposes detail history comparison and backfill routes", ApiRoutesExist)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Execute();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        var failure = $"FAIL {test.Name}: {exception.Message}";
        failures.Add(failure);
        Console.WriteLine(failure);
    }
}

if (failures.Count > 0)
{
    throw new InvalidOperationException(
        $"{failures.Count} robust integration test(s) failed.{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
}

Console.WriteLine($"All {tests.Length} robust pick evaluation integration tests passed.");

static Task MigrationIsAppendOnly()
{
    var path = FindRepositoryFile("CornersPredictionApi", "sql", "20260829_robust_pick_evaluation.sql");
    var sql = File.ReadAllText(path);

    Check.Contains(sql, "CREATE TABLE dbo.AutomatedBotPickRobustEvaluations");
    Check.Contains(sql, "CREATE TABLE dbo.AutomatedBotPickRobustComponents");
    Check.Contains(sql, "CREATE TABLE dbo.AutomatedBotRobustPolicies");
    Check.Contains(sql, "FK_RobustEvaluations_SourceEvaluation FOREIGN KEY");
    Check.Contains(sql, "REFERENCES dbo.AutomatedBotPickEvaluations");
    Check.Contains(sql, "FK_RobustEvaluations_Selection FOREIGN KEY");
    Check.Contains(sql, "REFERENCES dbo.AutomatedCornerBetSelections");
    Check.Contains(sql, "FK_RobustEvaluations_OddsSnapshot FOREIGN KEY");
    Check.Contains(sql, "REFERENCES dbo.CornerOddsSnapshots");
    Check.Contains(sql, "FK_RobustEvaluations_Supersedes FOREIGN KEY");
    Check.Contains(sql, "REFERENCES dbo.AutomatedBotPickRobustEvaluations");

    Check.Contains(sql, "CREATE OR ALTER TRIGGER dbo.trg_RobustEvaluations_AppendOnly");
    Check.Contains(sql, "AFTER UPDATE, DELETE");
    Check.Contains(sql, "Only an IsCurrent 1 to 0 supersession transition is allowed.");
    Check.Contains(sql, "CREATE OR ALTER TRIGGER dbo.trg_RobustComponents_Immutable");
    Check.Contains(sql, "CREATE OR ALTER TRIGGER dbo.trg_RobustPolicies_Immutable");

    Check.Contains(sql, "CREATE OR ALTER PROCEDURE dbo.sp_AppendAutomatedBotPickRobustEvaluation");
    Check.Contains(sql, "UQ_RobustEvaluations_Idempotency UNIQUE (IdempotencyHash)");
    Check.Contains(sql, "WHERE IdempotencyHash = @IdempotencyHash");
    Check.Contains(sql, "WHERE LogicalPickKey = @LogicalPickKey AND IsCurrent = 1");
    Check.Contains(sql, "@ExistingSnapshotHash <> @SnapshotHash");
    Check.Contains(sql, "SET IsCurrent = 0");
    Check.Contains(sql, "@PreviousId AS SupersedesEvaluationId");
    Check.Contains(sql, "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE");
    Check.Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");

    Check.DoesNotContain(sql, "DROP TABLE dbo.AutomatedBotPickRobust", StringComparison.OrdinalIgnoreCase);
    Check.DoesNotContain(sql, "TRUNCATE TABLE", StringComparison.OrdinalIgnoreCase);
    Check.DoesNotContain(sql, "DELETE FROM dbo.AutomatedBotPickRobustEvaluations", StringComparison.OrdinalIgnoreCase);
    Check.DoesNotContain(sql, "DELETE FROM dbo.AutomatedBotPickRobustComponents", StringComparison.OrdinalIgnoreCase);
    return Task.CompletedTask;
}

static Task MigrationParsesWithScriptDom()
{
    var path = FindRepositoryFile("CornersPredictionApi", "sql", "20260829_robust_pick_evaluation.sql");
    var parser = new TSql160Parser(initialQuotedIdentifiers: true);
    using var reader = new StringReader(File.ReadAllText(path));
    _ = parser.Parse(reader, out var errors);
    if (errors.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error =>
            $"L{error.Line},C{error.Column} SQL{error.Number}: {error.Message}")));
    }

    return Task.CompletedTask;
}

static Task RepositoryResidualQueryIsTemporal()
{
    var path = FindRepositoryFile(
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerRobustPickEvaluationRepository.cs");
    var source = File.ReadAllText(path);
    var method = Slice(
        source,
        "public async Task<IReadOnlyList<RobustResidualObservation>> LoadResidualHistoryAsync(",
        "public async Task<IReadOnlyList<OpenPortfolioExposureDto>> LoadOpenExposureAsync(");
    var query = Slice(
        source,
        "private const string ResidualHistorySql = \"\"\"",
        "private const string OpenExposureSql = \"\"\"");

    // Prediction evidence must predate both the evaluation and kickoff. Training
    // through <= prediction is stronger than merely <= evaluation because the
    // prediction itself is also strictly before EvaluationAsOfUtc.
    Check.Contains(query, "evaluation.PredictionTimestampUtc < @EvaluationAsOfUtc");
    Check.Contains(query, "evaluation.PredictionTimestampUtc < evaluation.MatchDate");
    Check.Contains(query, "evaluation.MatchDate < @EvaluationAsOfUtc");
    Check.Contains(query, "evaluation.BaseModelTrainedThroughUtc IS NOT NULL");
    Check.Contains(query, "evaluation.BaseModelTrainedThroughUtc <= evaluation.PredictionTimestampUtc");

    // Outcomes fail closed on a real provider/update availability timestamp. The
    // repository neither treats kickoff as final whistle nor invents an outcome
    // timestamp; the domain lag is supplied separately for a real fixture-end
    // fallback and is not double-applied to explicit availability evidence.
    Check.Contains(query,
        "OutcomeAvailableUtc = COALESCE(sourceOutcome.OutcomeAvailableUtc, history.ApiFootballUpdatedAtUtc)");
    Check.Contains(query, "OutcomeAvailableUtc IS NOT NULL");
    Check.Contains(query, "OutcomeAvailableUtc <= @EvaluationAsOfUtc");
    Check.Contains(query, "OutcomeAvailableUtc > PredictionAsOfUtc");
    Check.Contains(query, "OutcomeAvailableUtc >= FixtureStartUtc");
    Check.Contains(query, "OutcomeAvailabilityLagHours is only for a fixture-end fallback");
    Check.Contains(method, "query.OutcomeAvailabilityLagHours");
    Check.Contains(source, "query.OutcomeAvailabilityLagHours is < 0 or > 168");
    Check.DoesNotContain(
        query,
        "DATEADD(HOUR, @OutcomeAvailabilityLagHours, evaluation.MatchDate)",
        StringComparison.OrdinalIgnoreCase);

    // Family isolation exists at candidate admission and final projection, so a
    // fallback level can broaden market/scope without ever crossing families.
    Check.Contains(query, "evaluation.MarketFamily = @MarketFamily");
    Check.Contains(query, "NULLIF(evaluation.MarketFamily, N'') IS NULL");
    Check.Contains(query, "END = @MarketFamily");
    Check.Contains(query, "WHERE MarketFamily = @MarketFamily");

    // Cancellation is propagated into Dapper/SqlClient, not checked only before
    // opening the connection.
    Check.Contains(method, "new CommandDefinition(");
    Check.Contains(method, "cancellationToken: cancellationToken");
    return Task.CompletedTask;
}

static Task RepositoryBackfillPreviewIsSargable()
{
    var repositoryPath = FindRepositoryFile(
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerRobustPickEvaluationRepository.cs");
    var source = File.ReadAllText(repositoryPath);
    var query = Slice(
        source,
        "private const string BackfillPreviewSql = \"\"\"",
        "private const string BackfillCandidatesSql = \"\"\"");

    // The two branches are disjoint and preserve COALESCE's legacy fallback
    // semantics while allowing a range seek on each physical timestamp column.
    Check.Contains(query, "WITH DateScope AS");
    Check.Contains(query, "evaluation.PredictionTimestampUtc >= @FromUtc");
    Check.Contains(query, "evaluation.PredictionTimestampUtc < @ToUtc");
    Check.Contains(query, "UNION ALL");
    Check.Contains(query, "evaluation.PredictionTimestampUtc IS NULL");
    Check.Contains(query, "evaluation.EvaluatedAtUtc >= @FromUtc");
    Check.Contains(query, "evaluation.EvaluatedAtUtc < @ToUtc");
    Check.Contains(query, "FROM DateScope AS dateScope");
    Check.Contains(query, "OPTION (RECOMPILE)");
    Check.DoesNotContain(
        query,
        "COALESCE(evaluation.PredictionTimestampUtc, evaluation.EvaluatedAtUtc) >= @FromUtc",
        StringComparison.OrdinalIgnoreCase);

    var migrationPath = FindRepositoryFile(
        "CornersPredictionApi",
        "sql",
        "20260829_robust_pick_evaluation.sql");
    var migration = File.ReadAllText(migrationPath);
    Check.Contains(migration, "IX_AutomatedBotPickEvaluations_RobustBackfillPredictionTime");
    Check.Contains(migration, "WHERE PredictionTimestampUtc IS NOT NULL");
    Check.Contains(migration, "IX_AutomatedBotPickEvaluations_RobustBackfillFallbackTime");
    Check.Contains(migration, "INCLUDE (PredictionTimestampUtc)");
    Check.Contains(migration, "WHERE PredictionTimestampUtc IS NULL");
    return Task.CompletedTask;
}

static async Task FakeRepositoryAppendOnly()
{
    var repository = new InMemoryRobustRepository();
    var original = SnapshotCommand(
        subject: "fixture:7001|SHOTS|TotalShots|Under|24.5|Pinnacle",
        asOfUtc: Utc(2026, 8, 20, 10),
        payload: "{\"quote\":\"first\"}");

    var first = await repository.AppendAsync(original, CancellationToken.None);
    var duplicate = await repository.AppendAsync(original, CancellationToken.None);
    Check.True(first.Inserted);
    Check.Equal(1, first.EvaluationSequence);
    Check.False(duplicate.Inserted);
    Check.Equal(first.RobustEvaluationId, duplicate.RobustEvaluationId);
    Check.Equal(first.EvaluationSequence, duplicate.EvaluationSequence);

    var changed = SnapshotCommand(
        original.EvaluationSubjectKey,
        original.AsOfUtc.AddMinutes(2),
        "{\"quote\":\"moved\"}");
    var second = await repository.AppendAsync(changed, CancellationToken.None);
    Check.True(second.Inserted);
    Check.Equal(2, second.EvaluationSequence);
    Check.Equal(first.RobustEvaluationId, second.SupersedesEvaluationId);

    var history = await repository.GetHistoryBySelectionIdAsync(99, CancellationToken.None);
    Check.Equal(2, history.Count);
    Check.False(history[0].IsCurrent);
    Check.True(history[1].IsCurrent);
    Check.Equal(first.RobustEvaluationId, history[1].SupersedesEvaluationId);
    Check.Equal(1, history.Count(item => item.IsCurrent));
    Check.Equal(64, history[0].IdempotencyHash.Length);
    Check.Equal(64, history[1].SnapshotHash.Length);

    var current = await repository.GetCurrentBySelectionIdAsync(99, CancellationToken.None);
    Check.NotNull(current);
    Check.Equal(second.RobustEvaluationId, current!.Evaluation.RobustEvaluationId);
    Check.Equal("{\"quote\":\"first\"}", history[0].InputPayloadJson,
        "A superseding append must not rewrite the first snapshot payload.");
}

static async Task EvaluationModesAreEffective()
{
    var repository = new InMemoryRobustRepository();
    var service = BuildEvaluationService(repository);
    var input = EvaluationInput(7002, 12002, EvaluationMode.Shadow);

    var shadow = await service.EvaluateAsync(input, persist: false, CancellationToken.None);
    Check.NotNull(shadow);
    Check.Equal(EvaluationMode.Shadow, shadow!.Decision.Mode);
    Check.Equal(RobustDecision.Reject, shadow.Decision.RobustDecision,
        "The deliberately strict deterministic policy must reject the robust candidate.");
    Check.Equal(RobustDecision.Approve, shadow.Decision.EffectiveDecision,
        "Shadow must preserve the current BET behavior.");
    Check.Equal(input.OriginalStake, shadow.Decision.EffectiveStake);
    Check.False(shadow.Decision.ChangesCurrentBehavior);
    Check.Equal("Shadow", shadow.Snapshot.EvaluationMode);

    var enforce = await service.EvaluateAsync(
        EvaluationInput(7002, 12002, EvaluationMode.Enforce),
        persist: false,
        CancellationToken.None);
    Check.NotNull(enforce);
    Check.Equal(EvaluationMode.Enforce, enforce!.Decision.Mode);
    Check.Equal(RobustDecision.Reject, enforce.Decision.RobustDecision);
    Check.Equal(RobustDecision.Reject, enforce.Decision.EffectiveDecision);
    Check.Equal(0m, enforce.Decision.EffectiveStake);
    Check.True(enforce.Decision.ChangesCurrentBehavior);
    Check.Equal("Enforce", enforce.Snapshot.EvaluationMode);
}

static async Task OptionalScenarioProviderFailureIsIsolated()
{
    var repository = new InMemoryRobustRepository();
    var service = BuildEvaluationService(repository, [new ThrowingScenarioProvider()]);
    var result = await service.EvaluateAsync(
        EvaluationInput(7003, 12003, EvaluationMode.Shadow),
        persist: false,
        CancellationToken.None);
    Check.NotNull(result);
    Check.Contains(result!.Snapshot.EvaluationPayloadJson, "SCENARIO_PROVIDER_SOURCE_UNAVAILABLE");
    Check.Contains(result.Snapshot.EvaluationPayloadJson, "SourceUnavailable");
}

static async Task ScenarioSideUsesMarketLine()
{
    var repository = new InMemoryRobustRepository();
    var service = BuildEvaluationService(repository);
    var result = await service.EvaluateAsync(
        EvaluationInput(
            7004,
            12004,
            EvaluationMode.Shadow,
            rawProbability: 0.46m,
            calibratedProbability: 0.45m,
            probabilityLowerBound: 0.40m,
            probabilityUpperBound: 0.49m),
        persist: false,
        CancellationToken.None);

    Check.NotNull(result);
    Check.Equal(1m, result!.Value.ScenarioSideStability,
        "An Under prediction below the line retains its side even when its probability is below 50%." );
    Check.Equal(1m, result.Snapshot.ScenarioStability);
}

static async Task BackfillPreservesEvidence()
{
    var candidates = new[]
    {
        BackfillCandidate(201, 9201, Utc(2026, 8, 21, 5), Utc(2026, 8, 21, 4, 58)),
        BackfillCandidate(202, 9202, Utc(2026, 8, 21, 5, 10), Utc(2026, 8, 21, 5, 8)),
        BackfillCandidate(203, 0, Utc(2026, 8, 21, 5, 20), Utc(2026, 8, 21, 5, 18))
    };
    var repository = new InMemoryRobustRepository(candidates);
    var recorder = new RecordingEvaluationService(BuildEvaluationService(repository));
    var backfill = new RobustPickBackfillService(
        repository,
        recorder,
        NullLogger<RobustPickBackfillService>.Instance);
    var request = BackfillRequest();

    var first = await backfill.ExecuteAsync(request, CancellationToken.None);
    Check.False(first.DryRun);
    Check.Equal(2, first.Loaded);
    Check.Equal(2, first.Evaluated);
    Check.Equal(2, first.Inserted);
    Check.Equal(0, first.Idempotent);
    Check.Equal(0, first.Failures.Count);
    Check.Equal(202L, first.Checkpoint.SourceEvaluationId);
    Check.Equal(2, recorder.Inputs.Count);
    Check.False(recorder.Inputs.Any(item => item.SourceEvaluationId == 203),
        "A candidate without an exact immutable odds snapshot must never reach evaluation.");

    foreach (var candidate in candidates.Take(2))
    {
        var mapped = recorder.Inputs.Single(item => item.SourceEvaluationId == candidate.SourceEvaluationId);
        Check.Equal(candidate.PredictionTimestampUtc, mapped.PredictionAsOfUtc);
        Check.Equal(candidate.PredictionTimestampUtc, mapped.EvaluationAsOfUtc,
            "Historical evaluation must use the original pre-match AsOf, never the replay wall clock.");
        Check.Equal(candidate.SourceOddsSnapshotId, mapped.SourceOddsSnapshotId);
        Check.Equal(candidate.OddsTimestampUtc, mapped.QuoteTimestampUtc);
        Check.Equal(candidate.OverOdds, mapped.OverOdds);
        Check.Equal(candidate.UnderOdds, mapped.UnderOdds);
        Check.Equal(EvaluationMode.Shadow, mapped.EvaluationModeOverride);
    }

    recorder.Inputs.Clear();
    var replay = await backfill.ExecuteAsync(request, CancellationToken.None);
    Check.Equal(2, replay.Loaded);
    Check.Equal(0, replay.Inserted);
    Check.Equal(2, replay.Idempotent);
    Check.Equal(2, repository.History.Count,
        "Replaying identical inputs must not append duplicate snapshots.");
}

static async Task BackfillDryRunDoesNotWrite()
{
    var candidates = new[]
    {
        BackfillCandidate(191, 9191, Utc(2026, 8, 20, 5), Utc(2026, 8, 20, 4, 58)),
        BackfillCandidate(192, 9192, Utc(2026, 8, 20, 5, 10), Utc(2026, 8, 20, 5, 8))
    };
    var repository = new InMemoryRobustRepository(candidates);
    var recorder = new RecordingEvaluationService(BuildEvaluationService(repository));
    var backfill = new RobustPickBackfillService(
        repository,
        recorder,
        NullLogger<RobustPickBackfillService>.Instance);
    var request = BackfillRequest() with
    {
        Filter = BackfillRequest().Filter with { DryRun = true }
    };

    var result = await backfill.ExecuteAsync(request, CancellationToken.None);

    Check.True(result.DryRun);
    Check.Equal(2L, result.Preview.EligibleCandidates);
    Check.Equal(0, result.Loaded);
    Check.Equal(0, result.Evaluated);
    Check.Equal(0, result.Inserted);
    Check.Equal(0, recorder.Inputs.Count);
    Check.Equal(0, repository.History.Count);
    Check.Contains(result.Message, "No robust evaluation was appended");
}

static async Task BackfillStopsAtFailureCheckpoint()
{
    var candidates = new[]
    {
        BackfillCandidate(301, 9301, Utc(2026, 8, 22, 5), Utc(2026, 8, 22, 4, 58)),
        BackfillCandidate(302, 9302, Utc(2026, 8, 22, 5, 10), Utc(2026, 8, 22, 5, 8)),
        BackfillCandidate(303, 9303, Utc(2026, 8, 22, 5, 20), Utc(2026, 8, 22, 5, 18))
    };
    var repository = new InMemoryRobustRepository(candidates);
    var recorder = new RecordingEvaluationService(BuildEvaluationService(repository), failOnSourceEvaluationId: 302);
    var backfill = new RobustPickBackfillService(
        repository,
        recorder,
        NullLogger<RobustPickBackfillService>.Instance);

    var result = await backfill.ExecuteAsync(BackfillRequest(), CancellationToken.None);
    Check.Equal(2, result.Loaded);
    Check.Equal(1, result.Inserted);
    Check.Equal(1, result.Failures.Count);
    Check.Equal(302L, result.Failures[0].SourceEvaluationId);
    Check.Equal(301L, result.Checkpoint.SourceEvaluationId);
    Check.Equal(candidates[0].PredictionTimestampUtc, result.Checkpoint.PredictionTimestampUtc);
    Check.False(recorder.Inputs.Any(item => item.SourceEvaluationId == 303),
        "Backfill must stop at the first failure so its checkpoint is safe to resume.");
}

static Task ApiRoutesExist()
{
    var controller = typeof(RobustPickEvaluationsController);
    var route = controller.GetCustomAttribute<RouteAttribute>();
    Check.NotNull(route);
    Check.Equal("api/robust-pick-evaluations", route!.Template);

    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetDetail), "{selectionId:long}");
    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetHistory), "{selectionId:long}/history");
    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetComparison), "{selectionId:long}/comparison");
    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetMetrics), "metrics");
    CheckRoute<HttpPostAttribute>(controller, nameof(RobustPickEvaluationsController.Backfill), "backfill");
    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetEffectivePolicy), "policies/effective");
    CheckRoute<HttpGetAttribute>(controller, nameof(RobustPickEvaluationsController.GetPolicyHistory), "policies/history");
    CheckRoute<HttpPostAttribute>(controller, nameof(RobustPickEvaluationsController.AppendPolicy), "policies");
    return Task.CompletedTask;
}

static void CheckRoute<TAttribute>(Type controller, string methodName, string template)
    where TAttribute : HttpMethodAttribute
{
    var method = controller.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
    Check.NotNull(method, $"Action {methodName} was not found.");
    var attribute = method!.GetCustomAttribute<TAttribute>();
    Check.NotNull(attribute, $"Action {methodName} is missing {typeof(TAttribute).Name}.");
    Check.Equal(template, attribute!.Template);
}

static RobustPickEvaluationService BuildEvaluationService(
    InMemoryRobustRepository repository,
    IEnumerable<IScenarioProvider>? scenarioProviders = null)
{
    var options = Options.Create(new RobustPickEvaluationOptions
    {
        Enabled = true,
        Mode = "Shadow",
        Version = "robust-integration-test-v1",
        SimulationCount = 100,
        OuterScenarioCount = 20,
        EvaluationTimeoutSeconds = 10,
        DefaultMaxOddsAgeSeconds = 1_800,
        Residuals = new RobustResidualOptions
        {
            MinimumEffectiveN = 0m,
            TargetEffectiveN = 20m,
            RecencyHalfLifeDays = 90m,
            ErrorScaleEpsilon = 0.000001m
        },
        Policy = new RobustPolicyOptions
        {
            MinRobustEdge = 0.99m,
            MinRobustExpectedValue = 0.99m,
            MinPositiveEvStability = 0.99m,
            MinScenarioSideStability = 0.99m,
            MinNormalizedWorstCaseDistance = 0m,
            MaxNormalizedConsensusRange = 100m,
            MaxNormalizedCoherenceGap = 100m,
            MinCalibrationReliability = 0m,
            RequireSideAgreement = false
        },
        Exposure = new RobustExposureOptions { Enabled = false }
    });

    return new RobustPickEvaluationService(
        options,
        repository,
        new PredictionConsensusService(),
        new PredictionReconciliationService(),
        new EmpiricalResidualBootstrapV1(),
        new AsianValueCalculator(),
        new RobustMarketProbabilityService(),
        new CalibrationReliabilityService(),
        new RobustValueEvaluationService(),
        new RiskAdjustedStakeService(),
        new PortfolioExposureService(),
        new RobustPickPolicyEvaluator(),
        scenarioProviders ?? Array.Empty<IScenarioProvider>(),
        NullLogger<RobustPickEvaluationService>.Instance);
}

static RobustPickEvaluationInput EvaluationInput(
    long fixtureId,
    long sourceOddsSnapshotId,
    EvaluationMode mode,
    decimal rawProbability = 0.61m,
    decimal calibratedProbability = 0.60m,
    decimal probabilityLowerBound = 0.54m,
    decimal probabilityUpperBound = 0.65m)
{
    var asOf = Utc(2026, 8, 20, 10);
    return new RobustPickEvaluationInput
    {
        SourceEvaluationId = fixtureId,
        BotPickSelectionId = fixtureId + 10_000,
        SourceOddsSnapshotId = sourceOddsSnapshotId,
        EvaluationSubjectKey = $"source-evaluation:{fixtureId}",
        BotKey = "C2026",
        MarketFamily = "SHOTS",
        MarketType = "TotalShots",
        SelectedSide = "Under",
        League = "Integration League",
        HomeTeam = "Home",
        AwayTeam = "Away",
        Bookmaker = "Pinnacle",
        AutomationVersion = "AutomatedShotsBotV1-C2026",
        FixtureId = fixtureId,
        ExternalFixtureId = fixtureId + 1_000_000,
        FixtureStartUtc = asOf.AddHours(8),
        PredictionAsOfUtc = asOf,
        EvaluationAsOfUtc = asOf,
        QuoteTimestampUtc = asOf.AddMinutes(-2),
        Line = 24.5m,
        SelectedOdds = 2.02m,
        OverOdds = 1.92m,
        UnderOdds = 2.02m,
        OriginalStake = 1m,
        CurrentMinimumPointEdge = 0m,
        CurrentMinimumPointExpectedValue = 0m,
        CurrentDecision = CurrentSystemDecision.Bet,
        EvaluationModeOverride = mode,
        PrimaryPrediction = 22.13m,
        DirectPrediction = 22.13m,
        HomePrediction = 11.96m,
        AwayPrediction = 11.53m,
        ContextPrediction = 23.71m,
        ConfiguredModelMae = 4.5m,
        RawProbability = rawProbability,
        CalibratedProbability = calibratedProbability,
        ProbabilityLowerBound = probabilityLowerBound,
        ProbabilityUpperBound = probabilityUpperBound,
        DataQualityScore = 0.95m,
        BaseModelVersion = "shots-integration-model-v1",
        ModelTrainedThroughUtc = asOf.AddDays(-30),
        SelectorVersion = "selector-integration-v1",
        CalibrationVersion = "calibration-integration-v1",
        CalibrationEffectiveN = 150m,
        CalibrationExactMarketN = 150,
        CalibrationFamilyN = 250,
        CalibrationGlobalN = 500,
        CalibrationFallbackLevel = CalibrationFallbackLevel.ExactMarket,
        CalibrationEvidenceAgeDays = 2m,
        CalibrationError = 0.04m,
        IntelligenceEvidenceStatus = EvidenceStatus.ReviewedNeutral,
        LineupStatus = nameof(EvidenceStatus.ReviewedNeutral),
        FatigueDataStatus = nameof(EvidenceStatus.NotApplicable),
        GameStateModelStatus = nameof(EvidenceStatus.NotApplicable)
    };
}

static AppendRobustPickEvaluationCommand SnapshotCommand(
    string subject,
    DateTime asOfUtc,
    string payload) => new()
{
    SourceEvaluationId = 501,
    BotPickSelectionId = 99,
    SourceOddsSnapshotId = 8001,
    FixtureId = 9001,
    EvaluationSubjectKey = subject,
    BotKey = "C2026",
    MarketFamily = "SHOTS",
    MarketType = "TotalShots",
    Side = "Under",
    Line = 24.5m,
    Odds = 2.02m,
    Bookmaker = "Pinnacle",
    EvaluationVersion = "robust-integration-test-v1",
    AsOfUtc = asOfUtc,
    RobustnessVersion = "robust-integration-test-v1",
    PolicyVersion = "policy-integration-test-v1",
    EvaluationMode = "Shadow",
    CurrentSystemDecision = "Bet",
    RobustDecision = "Reject",
    OriginalStake = 1m,
    RecommendedStake = 0m,
    StakeMultiplier = 0m,
    InputPayloadJson = payload,
    EvaluationPayloadJson = "{}"
};

static RobustBackfillExecutionRequest BackfillRequest() => new(
    new RobustBackfillPreviewFilter(
        Utc(2026, 8, 1, 0),
        Utc(2026, 9, 1, 0),
        null,
        "SHOTS",
        "TotalShots",
        null,
        "robust-integration-test-v1",
        DryRun: false,
        Force: true),
    BatchSize: 2,
    MaximumCandidates: 100);

static RobustBackfillCandidateDto BackfillCandidate(
    long sourceEvaluationId,
    long sourceOddsSnapshotId,
    DateTime predictionUtc,
    DateTime oddsUtc) => new()
{
    SourceEvaluationId = sourceEvaluationId,
    PublishedSelectionId = sourceEvaluationId + 10_000,
    SourceOddsSnapshotId = sourceOddsSnapshotId,
    FixtureId = sourceEvaluationId + 20_000,
    ExternalFixtureId = sourceEvaluationId + 30_000,
    PartidoProximoCuotaId = sourceEvaluationId + 40_000,
    MatchDateUtc = predictionUtc.AddHours(10),
    PredictionTimestampUtc = predictionUtc,
    OddsTimestampUtc = oddsUtc,
    BotKey = "C2026",
    AutomationVersion = "AutomatedShotsBotV1-C2026",
    Decision = "Approved",
    League = "Integration League",
    HomeTeam = $"Home {sourceEvaluationId}",
    AwayTeam = $"Away {sourceEvaluationId}",
    Bookmaker = "Pinnacle",
    SourceMatchId = sourceEvaluationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    MarketFamily = "SHOTS",
    SourceMarketType = "TotalShots",
    MarketType = "TotalShots",
    Side = "Under",
    Line = 24.5m,
    SelectedOdds = 2.02m,
    OverOdds = 1.92m,
    UnderOdds = 2.02m,
    PrimaryPrediction = 22.13m,
    DirectPrediction = 22.13m,
    ContextPrediction = 23.71m,
    HomePrediction = 11.96m,
    AwayPrediction = 11.53m,
    RawProbability = 0.61m,
    CalibratedProbability = 0.60m,
    ProbabilityLowerBound = 0.54m,
    ProbabilityUpperBound = 0.65m,
    DataQualityScore = 0.95m,
    OriginalStake = 1m,
    BaseModelVersion = "shots-integration-model-v1",
    ModelTrainedThroughUtc = predictionUtc.AddDays(-30),
    SelectorVersion = "selector-integration-v1",
    CalibrationVersion = "calibration-integration-v1",
    FeatureSnapshotJson = "{\"model\":{\"sigma\":4.5}}"
};

static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
    new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

static string Slice(string value, string startMarker, string endMarker)
{
    var start = value.IndexOf(startMarker, StringComparison.Ordinal);
    Check.True(start >= 0, $"Start marker was not found: {startMarker}");
    var end = value.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
    Check.True(end > start, $"End marker was not found after start: {endMarker}");
    return value[start..end];
}

static string FindRepositoryFile(params string[] segments)
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
        if (File.Exists(candidate)) return candidate;
        current = current.Parent;
    }
    throw new FileNotFoundException($"Repository file was not found: {string.Join('/', segments)}");
}

sealed class RecordingEvaluationService : IRobustPickEvaluationService
{
    private readonly IRobustPickEvaluationService _inner;
    private readonly long? _failOnSourceEvaluationId;

    public RecordingEvaluationService(
        IRobustPickEvaluationService inner,
        long? failOnSourceEvaluationId = null)
    {
        _inner = inner;
        _failOnSourceEvaluationId = failOnSourceEvaluationId;
    }

    public List<RobustPickEvaluationInput> Inputs { get; } = [];

    public Task<RobustPickEvaluationExecution?> EvaluateAsync(
        RobustPickEvaluationInput input,
        bool persist,
        CancellationToken cancellationToken)
    {
        Inputs.Add(input);
        if (input.SourceEvaluationId == _failOnSourceEvaluationId)
            throw new InvalidOperationException("Injected deterministic backfill failure.");
        return _inner.EvaluateAsync(input, persist, cancellationToken);
    }

    public Task<AppendRobustEvaluationResult> PersistAsync(
        RobustPickEvaluationExecution execution,
        long? botPickSelectionId,
        CancellationToken cancellationToken) =>
        _inner.PersistAsync(execution, botPickSelectionId, cancellationToken);
}

sealed class InMemoryRobustRepository : IRobustPickEvaluationRepository
{
    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);
    private readonly List<StoredSnapshot> _history = [];
    private readonly Dictionary<string, StoredSnapshot> _byIdempotency =
        new(StringComparer.Ordinal);
    private readonly IReadOnlyList<RobustBackfillCandidateDto> _backfillCandidates;
    private long _nextEvaluationId = 1;
    private long _nextPolicyId = 1;

    public InMemoryRobustRepository(IReadOnlyList<RobustBackfillCandidateDto>? backfillCandidates = null)
    {
        _backfillCandidates = backfillCandidates ?? [];
    }

    public IReadOnlyList<RobustPickEvaluationSnapshot> History =>
        _history.OrderBy(item => item.Id).Select(ToSnapshot).ToArray();

    public Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<AppendRobustEvaluationResult> AppendAsync(
        AppendRobustPickEvaluationCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clone = JsonSerializer.Deserialize<AppendRobustPickEvaluationCommand>(
            JsonSerializer.Serialize(command, CloneOptions),
            CloneOptions) ?? throw new InvalidOperationException("Could not clone append command.");
        var logicalKey = Sha256(command.EvaluationSubjectKey);
        var inputHash = Sha256(JsonSerializer.Serialize(command, CloneOptions));
        var idempotency = Sha256($"{logicalKey}|{inputHash}|{command.EvaluationVersion}");
        if (_byIdempotency.TryGetValue(idempotency, out var existing))
        {
            return Task.FromResult(new AppendRobustEvaluationResult(
                existing.Id,
                existing.Sequence,
                false,
                existing.SupersedesId));
        }

        var prior = _history
            .Where(item => item.LogicalKey == logicalKey && item.IsCurrent)
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefault();
        if (prior is not null) prior.IsCurrent = false;
        var sequence = _history
            .Where(item => item.LogicalKey == logicalKey)
            .Select(item => item.Sequence)
            .DefaultIfEmpty()
            .Max() + 1;
        var stored = new StoredSnapshot(
            _nextEvaluationId++,
            logicalKey,
            idempotency,
            inputHash,
            Sha256($"{inputHash}|{command.EvaluationPayloadJson}"),
            sequence,
            true,
            prior?.Id,
            clone,
            clone.AsOfUtc);
        _history.Add(stored);
        _byIdempotency[idempotency] = stored;
        return Task.FromResult(new AppendRobustEvaluationResult(
            stored.Id,
            stored.Sequence,
            true,
            stored.SupersedesId));
    }

    public Task<RobustPickEvaluationDetail?> GetCurrentBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _history.LastOrDefault(item =>
            item.IsCurrent && item.Command.BotPickSelectionId == selectionId);
        return Task.FromResult(current is null
            ? null
            : new RobustPickEvaluationDetail(ToSnapshot(current), current.Command.Components));
    }

    public Task<IReadOnlyList<RobustPickEvaluationSnapshot>> GetHistoryBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RobustPickEvaluationSnapshot> result = _history
            .Where(item => item.Command.BotPickSelectionId == selectionId)
            .OrderBy(item => item.Sequence)
            .Select(ToSnapshot)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<RobustEvaluationComparisonDto?> GetComparisonBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        var detail = await GetCurrentBySelectionIdAsync(selectionId, cancellationToken);
        if (detail is null) return null;
        var value = detail.Evaluation;
        return new RobustEvaluationComparisonDto
        {
            BotPickSelectionId = selectionId,
            RobustEvaluationId = value.RobustEvaluationId,
            EvaluationSequence = value.EvaluationSequence,
            EvaluationMode = value.EvaluationMode,
            CurrentDecision = value.CurrentSystemDecision,
            ShadowDecision = value.RobustDecision,
            OriginalStake = value.OriginalStake,
            RecommendedStake = value.RecommendedStake,
            StakeDifference = value.RecommendedStake - value.OriginalStake,
            RobustnessScore = value.RobustnessScore,
            RejectionReasonCodesJson = value.RejectionReasonCodesJson,
            WarningCodesJson = value.WarningCodesJson,
            HumanReadableReason = value.HumanReadableReason,
            AsOfUtc = value.AsOfUtc
        };
    }

    public Task<RobustEvaluationMetricsDto> GetMetricsAsync(
        RobustEvaluationMetricsFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RobustEvaluationMetricsDto { Evaluated = _history.Count });
    }

    public Task<RobustBackfillPreviewResult> PreviewBackfillAsync(
        RobustBackfillPreviewFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eligible = ExactBackfillCandidates(filter).LongCount();
        return Task.FromResult(new RobustBackfillPreviewResult
        {
            DryRun = filter.DryRun,
            SourceCandidates = _backfillCandidates.Count,
            EligibleCandidates = eligible,
            MissingImmutableOddsSnapshot = _backfillCandidates.LongCount(item => item.SourceOddsSnapshotId <= 0),
            OddsSnapshotAfterPrediction = _backfillCandidates.LongCount(item =>
                item.OddsTimestampUtc > item.PredictionTimestampUtc),
            MissingBilateralOdds = _backfillCandidates.LongCount(item =>
                item.OverOdds <= 1m || item.UnderOdds <= 1m),
            Message = "In-memory leakage-safe preview."
        });
    }

    public Task<IReadOnlyList<RobustBackfillCandidateDto>> LoadBackfillCandidatesAsync(
        RobustBackfillPreviewFilter filter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RobustBackfillCandidateDto> page = ExactBackfillCandidates(filter)
            .Take(batchSize)
            .ToArray();
        return Task.FromResult(page);
    }

    public Task<IReadOnlyList<RobustResidualObservation>> LoadResidualHistoryAsync(
        RobustResidualHistoryQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RobustResidualObservation>>([]);
    }

    public Task<IReadOnlyList<OpenPortfolioExposureDto>> LoadOpenExposureAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OpenPortfolioExposureDto>>([]);
    }

    public Task<AppendRobustPolicyResult> AppendPolicyAsync(
        AppendRobustPolicyCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AppendRobustPolicyResult(_nextPolicyId++, true));
    }

    public Task<RobustPolicySnapshot?> GetEffectivePolicyAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<RobustPolicySnapshot?>(null);
    }

    public Task<IReadOnlyList<RobustPolicySnapshot>> GetPolicyHistoryAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RobustPolicySnapshot>>([]);
    }

    private IEnumerable<RobustBackfillCandidateDto> ExactBackfillCandidates(
        RobustBackfillPreviewFilter filter) => _backfillCandidates
        .Where(item => item.PredictionTimestampUtc >= filter.FromUtc
            && item.PredictionTimestampUtc <= filter.ToUtc
            && item.SourceOddsSnapshotId > 0
            && item.OddsTimestampUtc <= item.PredictionTimestampUtc
            && item.SelectedOdds > 1m
            && item.OverOdds > 1m
            && item.UnderOdds > 1m
            && item.ModelTrainedThroughUtc <= item.PredictionTimestampUtc
            && (filter.BotKey is null || item.BotKey.Equals(filter.BotKey, StringComparison.OrdinalIgnoreCase))
            && (filter.MarketFamily is null || item.MarketFamily.Equals(filter.MarketFamily, StringComparison.OrdinalIgnoreCase))
            && (filter.MarketType is null || item.MarketType.Equals(filter.MarketType, StringComparison.OrdinalIgnoreCase))
            && (filter.FixtureId is null || item.FixtureId == filter.FixtureId)
            && IsAfterCheckpoint(item, filter))
        .OrderBy(item => item.PredictionTimestampUtc)
        .ThenBy(item => item.SourceEvaluationId);

    private static bool IsAfterCheckpoint(
        RobustBackfillCandidateDto item,
        RobustBackfillPreviewFilter filter)
    {
        if (!filter.AfterPredictionTimestampUtc.HasValue) return true;
        if (item.PredictionTimestampUtc > filter.AfterPredictionTimestampUtc.Value) return true;
        return item.PredictionTimestampUtc == filter.AfterPredictionTimestampUtc.Value
            && item.SourceEvaluationId > (filter.AfterSourceEvaluationId ?? 0);
    }

    private static RobustPickEvaluationSnapshot ToSnapshot(StoredSnapshot item)
    {
        var value = item.Command;
        return new RobustPickEvaluationSnapshot
        {
            RobustEvaluationId = item.Id,
            LogicalPickKey = item.LogicalKey,
            IdempotencyHash = item.IdempotencyHash,
            InputHash = item.InputHash,
            SnapshotHash = item.SnapshotHash,
            EvaluationSequence = item.Sequence,
            IsCurrent = item.IsCurrent,
            SupersedesEvaluationId = item.SupersedesId,
            CreatedAtUtc = item.CreatedAtUtc,
            SourceEvaluationId = value.SourceEvaluationId,
            BotPickSelectionId = value.BotPickSelectionId,
            SourceOddsSnapshotId = value.SourceOddsSnapshotId,
            FixtureId = value.FixtureId,
            EvaluationSubjectKey = value.EvaluationSubjectKey,
            BotKey = value.BotKey,
            MarketFamily = value.MarketFamily,
            MarketType = value.MarketType,
            Side = value.Side,
            Line = value.Line,
            Odds = value.Odds,
            Bookmaker = value.Bookmaker,
            EvaluationVersion = value.EvaluationVersion,
            AsOfUtc = value.AsOfUtc,
            BaseModelVersion = value.BaseModelVersion,
            ModelTrainedThroughUtc = value.ModelTrainedThroughUtc,
            RobustnessVersion = value.RobustnessVersion,
            PolicyVersion = value.PolicyVersion,
            EvaluationMode = value.EvaluationMode,
            CurrentSystemDecision = value.CurrentSystemDecision,
            RobustDecision = value.RobustDecision,
            OriginalStake = value.OriginalStake,
            RecommendedStake = value.RecommendedStake,
            StakeMultiplier = value.StakeMultiplier,
            RobustnessScore = value.RobustnessScore,
            RejectionReasonCodesJson = value.RejectionReasonCodesJson,
            WarningCodesJson = value.WarningCodesJson,
            HumanReadableReason = value.HumanReadableReason,
            InputPayloadJson = value.InputPayloadJson,
            EvaluationPayloadJson = value.EvaluationPayloadJson,
            Components = value.Components
        };
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record StoredSnapshot(
        long Id,
        string LogicalKey,
        string IdempotencyHash,
        string InputHash,
        string SnapshotHash,
        int Sequence,
        bool InitialIsCurrent,
        long? SupersedesId,
        AppendRobustPickEvaluationCommand Command,
        DateTime CreatedAtUtc)
    {
        public bool IsCurrent { get; set; } = InitialIsCurrent;
    }
}

sealed class ThrowingScenarioProvider : IScenarioProvider
{
    public ScenarioType ScenarioType => ScenarioType.Fatigue;

    public ScenarioProviderResult Evaluate(ScenarioProviderRequest request) =>
        throw new InvalidOperationException("simulated optional provider outage");
}

static class Check
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected false.");

    public static void NotNull(object? value, string? message = null) =>
        True(value is not null, message ?? "Expected a non-null value.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string value, string expected) =>
        True(value.Contains(expected, StringComparison.Ordinal), $"Expected SQL fragment: {expected}");

    public static void DoesNotContain(
        string value,
        string unexpected,
        StringComparison comparison) =>
        False(value.Contains(unexpected, comparison), $"Unexpected SQL fragment: {unexpected}");
}
