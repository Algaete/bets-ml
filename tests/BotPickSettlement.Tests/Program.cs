using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotC;
using CornersPrediction.Application.Automation.BotD;
using CornersPrediction.Application.Automation.BotE;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Application.Teams;
using CornersPrediction.Domain.FootballIntelligence;
using System.Text.RegularExpressions;

var tests = new (string Name, Action Execute)[]
{
    ("Zero is a real value for every supported scope", ZeroIsNotNull),
    ("Missing relevant statistic stays pending", MissingStatisticStaysPending),
    ("Integer Asian line produces Push", IntegerLineProducesPush),
    ("Quarter Asian lines produce half results", QuarterLinesProduceHalfResults),
    ("Every market uses the correct local statistic", EveryMarketUsesCorrectStatistic),
    ("Requested goal, corner and shots-on-goal examples", RequestedMarketExamples),
    ("Unfinished and missing-stat matches remain Pending", PendingCasesRemainPending),
    ("Only final fixture statuses can settle", OnlyFinalStatusesSettle),
    ("Unverified local history stays pending without fixture status", UnverifiedHistoricalRowStaysPending),
    ("Updated MatchHistory reconciles an automatic settlement", UpdatedMatchHistoryReconcilesSettlement),
    ("Selected bot and market scope is normalized and propagated", SelectedScopeIsPropagated),
    ("Invalid settlement scope is rejected", InvalidScopeIsRejected),
    ("Settlement is idempotent after a pick leaves Pending", SettlementIsIdempotent),
    ("Provider team aliases remain deterministic", ProviderTeamAliasesAreDeterministic)
    ,("Bot C weighted statistics are deterministic", BotCWeightedStatisticsAreDeterministic)
    ,("Bot C shrinkage and exact-line rates are correct", BotCShrinkageAndHitRatesAreCorrect)
    ,("Bot C excludes every observation at or after AsOfDateUtc", BotCPreventsTemporalLeakage)
    ,("Bot C approves a fully supported candidate", BotCApprovesSupportedCandidate)
    ,("Bot C marks missing history as PendingData", BotCMissingHistoryIsPending)
    ,("Bot C supports every configured market adapter", BotCSupportsEveryMarket)
    ,("Bot C reproduces the same decision", BotCDecisionIsReproducible)
    ,("Bot C rejects invalid configuration weights", BotCRejectsInvalidWeights)
    ,("Bot C uses a compatible meta-model probability", BotCUsesMetaModel)
    ,("Bot C rejects a schema mismatch when fallback is disabled", BotCRejectsSchemaMismatchWithoutFallback)
    ,("Bot C resolves market and selection thresholds deterministically", BotCResolvesMarketThresholds)
    ,("Bot C resolves calibration by market and selection", BotCResolvesCalibrationProfile)
    ,("Bot C rejects base-model temporal leakage", BotCRejectsBaseModelTemporalLeakage)
    ,("Bot D derives a positive gap through common opponents", BotDUsesCommonOpponents)
    ,("Bot D excludes future team-strength results", BotDPreventsTeamStrengthLeakage)
    ,("Bot D increases supported home-team probability", BotDAdjustsHomeTeamProbability)
    ,("Bot D reverses the gap direction for away-team markets", BotDAdjustsAwayTeamProbability)
    ,("Bot D manifest exposes the exhaustive strength calculation", BotDManifestIsExhaustive)
    ,("Bot E excludes outcomes that violate its availability lag", BotEPreventsTemporalLeakage)
    ,("Bot E counts each fixture as one independent observation", BotEDeduplicatesFixtureEvidence)
    ,("Bot E preserves exact Asian quarter-line returns", BotEPreservesAsianQuarterReturns)
    ,("Bot E rejects insufficient independent evidence", BotERejectsInsufficientEvidence)
    ,("Bot E rejects a collapsed effective sample", BotERejectsCollapsedEffectiveSample)
    ,("Bot E target evidence controls reliability", BotETargetEvidenceControlsReliability)
    ,("Bot E preserves uncertainty for identical outcomes", BotEUncertaintyDoesNotCollapse)
    ,("Bot E evidence hash changes after a result correction", BotEEvidenceHashTracksCorrections)
    ,("Bot E calibration is deterministic regardless of input order", BotECalibrationIsDeterministic)
    ,("Bot F calibration history uses its pre-calibration probability", BotFHistoryUsesPreCalibrationProbability)
    ,("Bot E and H calibration history preserves legacy base probability", BotEAndHHistoryPreservesBaseProbability)
    ,("Calibration history repository never feeds FinalProbability", CalibrationHistoryRepositoryAvoidsFinalProbability)
    ,("Production decisions use the exact immutable bilateral odds snapshot", ProductionUsesImmutableBilateralOddsSnapshot)
    ,("Bot C and D ignore calibration history while Bot E is disabled", BotCAndDRemainUnchangedWhenBotEIsDisabled)
    ,("Bot F legacy source requires version and temporal provenance", BotFLegacySourceRequiresProvenance)
    ,("Football intelligence is exactly neutral without usable evidence", FootballIntelligenceIsNeutralWithoutEvidence)
    ,("Football intelligence applies only bounded market-specific evidence", FootballIntelligenceAppliesBoundedMarketSignal)
    ,("Football intelligence reports the effective adjustment after probability clamping", FootballIntelligenceReportsEffectiveClampedAdjustment)
    ,("Every active built-in bot enables football intelligence while B stays retired", ActiveBotsEnableFootballIntelligence)
    ,("Bots E and F keep identical decisions when internet evidence is absent", BotEAndFRemainNumericallyUnchangedWithoutInternetEvidence)
    ,("Lexical news extraction only accepts explicit availability statements", NewsExtractionRequiresExplicitEvidence)
    ,("News queries balance English Spanish and Portuguese", NewsQueriesAreLanguageBalanced)
    ,("News facts deduplicate by player and official lineups prevail", NewsFactsConsolidateDeterministically)
    ,("Football intelligence SQL avoids entity Id parameter collisions", FootballIntelligenceSqlAvoidsIdParameterCollisions)
    ,("API-Football intelligence parser tolerates optional null numbers", FootballIntelligenceParserToleratesNullNumbers)
    ,("API-Football card absences are classified as suspensions", FootballIntelligenceClassifiesCardSuspensions)
    ,("Manual settlement SQL is auditable and excluded from automatic reconciliation", ManualSettlementSqlIsProtected)
    ,("Bot B is retired without deleting historical picks", BotBIsRetired)
    ,("Bot H is seeded as a valid shadow-only calibration challenger", BotHIsShadowOnlyChallenger)
    ,("Chile corners exclusion merges idempotently without replacing league filters", ChileCornersExclusionMergesIdempotently)
};

static void ProductionUsesImmutableBilateralOddsSnapshot()
{
    var repository = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "CornersPredictionApi",
        "Robot",
        "AutomatedCornersBot",
        "SqlAutomationRepository.cs"));
    var service = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "CornersPredictionApi",
        "Robot",
        "AutomatedCornersBot",
        "AutomatedCornersSelectionService.cs"));

    Assert(repository.Contains(
        "WHEN latestSnapshot.OddsSnapshotId IS NOT NULL THEN latestSnapshot.SnapshotOverOdds",
        StringComparison.Ordinal));
    Assert(repository.Contains(
        "WHEN latestSnapshot.OddsSnapshotId IS NOT NULL THEN latestSnapshot.SnapshotUnderOdds",
        StringComparison.Ordinal));
    Assert(service.Contains("candidate.Odds.SnapshotOverOdds is > 1m", StringComparison.Ordinal));
    Assert(service.Contains("candidate.Odds.SnapshotUnderOdds is > 1m", StringComparison.Ordinal));
    Assert(service.Contains(
        "EnsureUtc(bundle.Odds.OddsCapturedAtUtc ?? bundle.Odds.UpdatedAtUtc)",
        StringComparison.Ordinal));
    Assert(service.Contains(
        "var enforceLiveProductionGate = !effectiveRequest.DryRun && !historicalMode;",
        StringComparison.Ordinal));
}

static void ChileCornersExclusionMergesIdempotently()
{
    var schema = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "CornersPredictionApi", "sql", "automated_corners_bot.sql"));

    Assert(schema.Contains(
        "WHERE BotKey IN (N'A', N'B', N'C2026', N'D2026', N'E2026', N'F2026')",
        StringComparison.Ordinal));
    Assert(schema.Contains("THEN @ChileFilterJson", StringComparison.Ordinal));
    Assert(schema.Contains("FROM OPENJSON(@ChileMergedFilterJson)", StringComparison.Ordinal));
    Assert(schema.Contains("@CornerFilterPath", StringComparison.Ordinal));
    Assert(schema.Contains("JSON_MODIFY(\n                @ChileMergedFilterJson,\n                @CornerFilterPath",
        StringComparison.Ordinal));
    Assert(schema.Contains("@CornersFilterIndex = @FirstCornersFilterIndex", StringComparison.Ordinal));
    Assert(schema.Contains("COLLATE Latin1_General_100_CI_AI <> N'Chile - *'", StringComparison.Ordinal));
    Assert(!schema.Contains(
        "AND LeagueFilterJson IS NULL;",
        StringComparison.Ordinal));
}

static void BotHIsShadowOnlyChallenger()
{
    Assert(RecommendationBotLifecycle.IsShadowOnly("H2026"));
    var schema = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "CornersPredictionApi", "sql", "automated_corners_bot.sql"));
    Assert(schema.Contains("N'H2026' AS BotKey", StringComparison.Ordinal));
    Assert(schema.Contains("PublishEnabled = 0", StringComparison.Ordinal));
    var match = Regex.Match(
        schema,
        "N'(?<json>\\{\"configurationVersion\":\"bot-h-corners-calibration-shadow-1\\.0\\.0\".*?\\})' AS StrategyConfigurationJson",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);
    Assert(match.Success);
    var configuration = BotCStrategyConfiguration.FromJson(match.Groups["json"].Value.Replace("''", "'"));
    Assert(configuration.EmpiricalCalibration.Enabled);
    Assert(configuration.EmpiricalCalibration.SourceBotKey == "C2026");
    Assert(configuration.ResolveThresholds("TotalCorners", "Over").Enabled == false);
    Assert(configuration.ResolveThresholds("HomeTeamCorners", "Over").MinimumFinalEdge >= 0.04d);
}

static void FootballIntelligenceClassifiesCardSuspensions()
{
    Assert(StructuredFootballEvidenceClassifier.IsSuspension("Missing Fixture", "Yellow Cards"));
    Assert(StructuredFootballEvidenceClassifier.IsSuspension(null, "Red Card"));
    Assert(StructuredFootballEvidenceClassifier.IsSuspension("Suspended", null));
    Assert(!StructuredFootballEvidenceClassifier.IsSuspension("Missing Fixture", "Groin Injury"));
}

foreach (var test in tests)
{
    test.Execute();
    Console.WriteLine($"PASS {test.Name}");
}

Console.WriteLine($"{tests.Length} Bot Pick settlement tests passed.");

static void BotFHistoryUsesPreCalibrationProbability()
{
    var asOf = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    var history = Enumerable.Range(1, 24)
        .Select(index => BotEObservation(
            8000 + index,
            9000 + index,
            asOf.AddDays(-index),
            side: "Under",
            line: 9.5m,
            actual: 8,
            sourceProbability: 0.90d))
        .ToArray();
    var configuration = new BotCStrategyConfiguration
    {
        ConfigurationVersion = "bot-f-recursive-calibration-regression",
        EmpiricalCalibration = BotETestConfiguration(
            minimumObservations: 2,
            minimumExactMarketObservations: 2,
            probabilityBandwidth: 0.50d)
    };
    var decision = new BotCPickDecisionEngine().Evaluate(
        BotCInput(asOf) with { CalibrationHistory = history },
        configuration);
    var probability = BotECalibrationSourceProbabilityResolver.Resolve(
        decision.FeatureSnapshotJson,
        decision.BaseCalibratedProbability);

    Assert(probability.HasValue);
    AssertClose(probability.GetValueOrDefault(), decision.BaseCalibratedProbability);
    Assert(Math.Abs(probability.GetValueOrDefault() - decision.FinalProbability) > 0.000001d);
}

static void BotEAndHHistoryPreservesBaseProbability()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var sourceCDecision = new BotCPickDecisionEngine().Evaluate(
        BotCInput(asOf),
        new BotCStrategyConfiguration());
    var currentSourceProbability = BotECalibrationSourceProbabilityResolver.Resolve(
        sourceCDecision.FeatureSnapshotJson,
        sourceCDecision.BaseCalibratedProbability);
    var legacySourceProbability = BotECalibrationSourceProbabilityResolver.Resolve(
        "{\"featureSchemaVersion\":\"legacy\"}",
        0.58d);
    var malformedLegacySourceProbability = BotECalibrationSourceProbabilityResolver.Resolve(
        "[]",
        0.57d);

    Assert(currentSourceProbability.HasValue);
    AssertClose(currentSourceProbability.GetValueOrDefault(), sourceCDecision.FinalProbability);
    Assert(legacySourceProbability.HasValue);
    AssertClose(legacySourceProbability.GetValueOrDefault(), 0.58d);
    Assert(malformedLegacySourceProbability.HasValue);
    AssertClose(malformedLegacySourceProbability.GetValueOrDefault(), 0.57d);
    Assert(BotECalibrationSourceProbabilityResolver.Resolve(
        "{\"empiricalCalibration\":{\"probabilityBeforeEmpiricalCalibration\":1.1}}",
        0.58d) is null);
}

static void CalibrationHistoryRepositoryAvoidsFinalProbability()
{
    var repository = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "CornersPredictionApi",
        "Robot",
        "AutomatedCornersBot",
        "SqlAutomationRepository.cs"));
    var methodStart = repository.IndexOf(
        "GetBotECalibrationHistoryAsync(",
        StringComparison.Ordinal);
    Assert(methodStart >= 0);
    var methodEnd = repository.IndexOf(
        "public async Task<UpsertSelectionResult>",
        methodStart,
        StringComparison.Ordinal);
    Assert(methodEnd > methodStart);
    var method = repository[methodStart..methodEnd];

    Assert(!method.Contains("SourceProbability = e.FinalProbability", StringComparison.Ordinal));
    Assert(method.Contains(
        "BotECalibrationSourceProbabilityResolver.Resolve",
        StringComparison.Ordinal));
    Assert(method.Contains("FeatureSnapshotJson", StringComparison.Ordinal));
}

static void BotBIsRetired()
{
    Assert(RecommendationBotLifecycle.IsRetired("B"));
    Assert(RecommendationBotLifecycle.IsRetired(" b "));
    Assert(!RecommendationBotLifecycle.IsRetired("A"));
    Assert(!RecommendationBotLifecycle.IsRetired("C2026"));

    var root = FindRepositoryRoot();
    var schema = File.ReadAllText(Path.Combine(
        root,
        "CornersPredictionApi",
        "sql",
        "automated_corners_bot.sql"));
    Assert(schema.Contains("WHERE BotKey = N'B'", StringComparison.Ordinal));
    Assert(schema.Contains("SET IsEnabled = 0", StringComparison.Ordinal));
}

static void ZeroIsNotNull()
{
    foreach (var market in SupportedMarkets())
    {
        var candidate = Candidate(market, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert(AutomatedBotPickSettlementCalculator.TryResolveActual(
            candidate, out var actual, out _, out _, out _, out _));
        Assert(actual == 0);
    }
}

static void ManualSettlementSqlIsProtected()
{
    var root = FindRepositoryRoot();
    var migration = File.ReadAllText(Path.Combine(
        root,
        "CornersPredictionApi",
        "sql",
        "20260818_fix_manual_bot_pick_settlement.sql"));
    var candidateSql = File.ReadAllText(Path.Combine(
        root,
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerAutomatedBotPickSettlementRepository.cs"));

    Assert(migration.Contains("SettlementActualValue = @ActualValue", StringComparison.Ordinal));
    Assert(migration.Contains("SettlementFactor = @SettlementFactor", StringComparison.Ordinal));
    Assert(migration.Contains("SettlementSource = N'Manual'", StringComparison.Ordinal));
    Assert(migration.Contains("LastSettlementCheckReason = @SettlementReason", StringComparison.Ordinal));
    Assert(migration.Contains("CREATE OR ALTER PROCEDURE dbo.sp_UpdateAutomatedCornerBetSelectionStatus", StringComparison.Ordinal));
    Assert(candidateSql.Contains(
        "s.SettlementSource IN (N'LocalMatchHistory', N'LocalMatchHistoryHistorical')",
        StringComparison.Ordinal));
}

static void FootballIntelligenceSqlAvoidsIdParameterCollisions()
{
    var root = FindRepositoryRoot();
    var repository = File.ReadAllText(Path.Combine(
        root,
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerFootballIntelligenceRepository.cs"));

    Assert(!repository.Contains("DECLARE @Id BIGINT", StringComparison.Ordinal));
    Assert(repository.Contains("DECLARE @PersistedId BIGINT", StringComparison.Ordinal));
}

static void FootballIntelligenceParserToleratesNullNumbers()
{
    var root = FindRepositoryRoot();
    var provider = File.ReadAllText(Path.Combine(
        root,
        "CornersPredictionApi",
        "FootballIntelligence",
        "ApiFootballStructuredDataProvider.cs"));

    Assert(provider.Contains(
        "value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined",
        StringComparison.Ordinal));
    Assert(provider.Contains(
        "value.ValueKind == JsonValueKind.Number && value.TryGetInt32",
        StringComparison.Ordinal));
    Assert(provider.Contains(
        "value.ValueKind == JsonValueKind.Number && value.TryGetInt64",
        StringComparison.Ordinal));
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CornersPrediction.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }

    directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CornersPrediction.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("CornersPrediction repository root was not found.");
}

static void MissingStatisticStaysPending()
{
    var candidate = Candidate("HomeTeamCorners", homeCorners: null);
    Assert(!AutomatedBotPickSettlementCalculator.TryResolveActual(
        candidate, out _, out _, out _, out _, out var reason));
    Assert(reason?.Contains("NULL no equivale a cero", StringComparison.Ordinal) == true);
}

static void IntegerLineProducesPush()
{
    var outcome = AutomatedBotPickSettlementCalculator.Calculate("Over", 3m, 3, 1.90m, 1m);
    Assert(outcome.Status == "Push");
    Assert(outcome.Factor == 0m);
    Assert(outcome.ProfitLoss == 0m);
}

static void QuarterLinesProduceHalfResults()
{
    var halfWin = AutomatedBotPickSettlementCalculator.Calculate("Under", 3.25m, 3, 1.80m, 1m);
    Assert(halfWin.Status == "Won");
    Assert(halfWin.Factor == 0.5m);
    Assert(halfWin.ProfitLoss == 0.40m);

    var halfLoss = AutomatedBotPickSettlementCalculator.Calculate("Under", 3.75m, 4, 1.80m, 1m);
    Assert(halfLoss.Status == "Lost");
    Assert(halfLoss.Factor == -0.5m);
    Assert(halfLoss.ProfitLoss == -0.50m);
}

static void EveryMarketUsesCorrectStatistic()
{
    var expected = new Dictionary<string, int>
    {
        ["HomeTeamGoals"] = 1, ["AwayTeamGoals"] = 2, ["TotalGoals"] = 3,
        ["HomeTeamCorners"] = 3, ["AwayTeamCorners"] = 4, ["TotalCorners"] = 7,
        ["HomeTeamShots"] = 5, ["AwayTeamShots"] = 6, ["TotalShots"] = 11,
        ["HomeTeamShotsOnGoal"] = 7, ["AwayTeamShotsOnGoal"] = 8, ["TotalShotsOnGoal"] = 15
    };
    var candidateTemplate = Candidate("TotalGoals", 1, 2, 3, 4, 5, 6, 7, 8);

    foreach (var pair in expected)
    {
        var candidate = candidateTemplate with { MarketType = pair.Key };
        Assert(AutomatedBotPickSettlementCalculator.TryResolveActual(
            candidate, out var actual, out _, out _, out _, out _));
        Assert(actual == pair.Value);
    }
}

static void RequestedMarketExamples()
{
    Assert(AutomatedBotPickSettlementCalculator.Calculate("Over", 2.5m, 3, 1.90m, 1m).Status == "Won");
    Assert(AutomatedBotPickSettlementCalculator.Calculate("Under", 2.5m, 3, 1.90m, 1m).Status == "Lost");
    Assert(AutomatedBotPickSettlementCalculator.Calculate("Over", 9.5m, 11, 1.90m, 1m).Status == "Won");
    Assert(AutomatedBotPickSettlementCalculator.Calculate("Over", 10m, 10, 1.90m, 1m).Status == "Push");
    Assert(AutomatedBotPickSettlementCalculator.Calculate("Over", 8.5m, 9, 1.90m, 1m).Status == "Won");
}

static void PendingCasesRemainPending()
{
    var unfinishedRepository = new FakeSettlementRepository(
        Candidate("TotalGoals", homeGoals: 2, awayGoals: 1) with { FixtureStatus = "1H" });
    var unfinished = new AutomatedBotPickSettlementUseCase(unfinishedRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(unfinished.Items.Single().Status == "Pending");

    var missingRepository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeCorners: null, awayCorners: null));
    var missing = new AutomatedBotPickSettlementUseCase(missingRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(missing.Items.Single().Status == "Pending");
}

static void OnlyFinalStatusesSettle()
{
    foreach (var status in new[] { "FT", "AET", "PEN" })
    {
        Assert(AutomatedBotPickFixtureStatusPolicy.IsFinished(status));
    }

    foreach (var status in new string?[] { null, "NS", "1H", "PST", "SUSP", "CANC" })
    {
        Assert(!AutomatedBotPickFixtureStatusPolicy.IsFinished(status));
    }
}

static void UnverifiedHistoricalRowStaysPending()
{
    var historicalRepository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeGoals: 1, awayGoals: 2, homeCorners: 2, awayCorners: 6) with
        {
            FixtureStatus = null,
            MatchDate = DateTime.UtcNow.AddDays(-10),
            LineValue = 7.5m
        });
    var historical = new AutomatedBotPickSettlementUseCase(historicalRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(historical.Items.Single().Status == "Pending");
    Assert(historical.Items.Single().Reason.Contains("no tiene un estado final verificable", StringComparison.Ordinal));

    var recentRepository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeGoals: 1, awayGoals: 2, homeCorners: 2, awayCorners: 6) with
        {
            FixtureStatus = null,
            MatchDate = DateTime.UtcNow
        });
    var recent = new AutomatedBotPickSettlementUseCase(recentRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(recent.Items.Single().Status == "Pending");

    var explicitNonFinalRepository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeGoals: 1, awayGoals: 2, homeCorners: 2, awayCorners: 6) with
        {
            FixtureStatus = "PST",
            MatchDate = DateTime.UtcNow.AddDays(-10)
        });
    var explicitNonFinal = new AutomatedBotPickSettlementUseCase(explicitNonFinalRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(explicitNonFinal.Items.Single().Status == "Pending");

    var zeroFilledHistoricalRepository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeGoals: 1, awayGoals: 0, homeCorners: 0, awayCorners: 0,
            homeShots: 0, awayShots: 0, homeShotsOnGoal: 0, awayShotsOnGoal: 0) with
        {
            FixtureStatus = null,
            MatchDate = DateTime.UtcNow.AddDays(-10),
            LineValue = 9m
        });
    var zeroFilledHistorical = new AutomatedBotPickSettlementUseCase(zeroFilledHistoricalRepository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(zeroFilledHistorical.Items.Single().Status == "Pending");
    Assert(zeroFilledHistorical.Items.Single().Reason.Contains("no tiene un estado final verificable", StringComparison.Ordinal));
}

static void SettlementIsIdempotent()
{
    var repository = new FakeSettlementRepository(Candidate("TotalGoals", homeGoals: 2, awayGoals: 1));
    var service = new AutomatedBotPickSettlementUseCase(repository);
    var first = service.SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();
    var second = service.SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();

    Assert(first.SettledRows == 1);
    Assert(second.ReviewedRows == 0);
    Assert(repository.ApplyCalls == 1);
}

static void UpdatedMatchHistoryReconcilesSettlement()
{
    var originalSettledAtUtc = DateTime.UtcNow.AddHours(-1);
    var repository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeCorners: 6, awayCorners: 6) with
        {
            SelectedSide = "Under",
            LineValue = 10.5m,
            ReconcileExistingSettlement = true,
            ExpectedSettledAtUtc = originalSettledAtUtc,
            SourceUpdatedAtUtc = DateTime.UtcNow
        });
    var result = new AutomatedBotPickSettlementUseCase(repository)
        .SettleAsync(new AutomatedBotPickSettlementRequest(), CancellationToken.None)
        .GetAwaiter().GetResult();

    Assert(result.Items.Single().Status == "Lost");
    Assert(result.Items.Single().ActualValue == 12);
    Assert(result.Items.Single().Reason.StartsWith("Reconciliado", StringComparison.Ordinal));
}

static void SelectedScopeIsPropagated()
{
    var repository = new FakeSettlementRepository(
        Candidate("TotalCorners", homeCorners: 4, awayCorners: 5));
    var result = new AutomatedBotPickSettlementUseCase(repository)
        .SettleAsync(
            new AutomatedBotPickSettlementRequest(
                DryRun: true,
                BotKey: " c ",
                MarketFamily: "corners"),
            CancellationToken.None)
        .GetAwaiter().GetResult();

    Assert(repository.LastFilter?.BotKey == "C2026");
    Assert(repository.LastFilter?.MarketFamily == "CORNERS");
    Assert(result.BotKey == "C2026");
    Assert(result.MarketFamily == "CORNERS");

    var botDRepository = new FakeSettlementRepository(
        Candidate("HomeTeamGoals", homeGoals: 2, awayGoals: 0));
    var botDResult = new AutomatedBotPickSettlementUseCase(botDRepository)
        .SettleAsync(
            new AutomatedBotPickSettlementRequest(DryRun: true, BotKey: " d ", MarketFamily: "goals"),
            CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(botDRepository.LastFilter?.BotKey == "D2026");
    Assert(botDResult.BotKey == "D2026");

    var botERepository = new FakeSettlementRepository(
        Candidate("TotalShots", homeShots: 9, awayShots: 7));
    var botEResult = new AutomatedBotPickSettlementUseCase(botERepository)
        .SettleAsync(
            new AutomatedBotPickSettlementRequest(DryRun: true, BotKey: " e ", MarketFamily: "shots"),
            CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(botERepository.LastFilter?.BotKey == "E2026");
    Assert(botEResult.BotKey == "E2026");

    var botFRepository = new FakeSettlementRepository(
        Candidate("AwayTeamShotsOnGoal", homeShotsOnGoal: 2, awayShotsOnGoal: 4));
    var botFResult = new AutomatedBotPickSettlementUseCase(botFRepository)
        .SettleAsync(
            new AutomatedBotPickSettlementRequest(DryRun: true, BotKey: " f ", MarketFamily: "sog"),
            CancellationToken.None)
        .GetAwaiter().GetResult();
    Assert(botFRepository.LastFilter?.BotKey == "F2026");
    Assert(botFResult.BotKey == "F2026");
}

static void InvalidScopeIsRejected()
{
    var repository = new FakeSettlementRepository();
    var service = new AutomatedBotPickSettlementUseCase(repository);

    try
    {
        service.SettleAsync(
                new AutomatedBotPickSettlementRequest(BotKey: "C;DROP", MarketFamily: "corners"),
                CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert(false);
    }
    catch (ArgumentException)
    {
    }

    try
    {
        service.SettleAsync(
                new AutomatedBotPickSettlementRequest(BotKey: "C", MarketFamily: "cards"),
                CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert(false);
    }
    catch (ArgumentException)
    {
    }
}

static void ProviderTeamAliasesAreDeterministic()
{
    Assert(TeamNameMatcher.FindBestMatch("Dundee United", ["Dundee Utd"])?.Confidence >= 0.96);
    Assert(TeamNameMatcher.FindBestMatch("Audax Italiano", ["A. Italiano"])?.Confidence >= 0.96);
    Assert(TeamNameMatcher.FindBestMatch("España", ["Spain"])?.Confidence >= 0.96);
    Assert(TeamNameMatcher.FindBestMatch("St Louis City SC", ["St. Louis City"])?.Confidence >= 0.96);
    Assert(TeamNameMatcher.FindBestMatch("Los Chankas", ["Club Deportivo Los Chankas"])?.Confidence >= 0.96);
    Assert(TeamNameMatcher.FindBestMatch("Coventry City", ["Coventry"])?.Confidence >= 0.98);
    Assert(TeamNameMatcher.FindBestMatch("Coventry", ["Coventry City"])?.Confidence >= 0.98);
    Assert(TeamNameMatcher.FindBestMatch("Vicenza", ["Vicenza Virtus"])?.Confidence == 1);
    Assert(TeamNameMatcher.FindBestMatch("Jaguares de Cordoba", ["Jaguares"])?.Confidence == 1);
    Assert(TeamNameMatcher.FindBestMatch("Boyaca Chico", ["Chico"])?.Confidence == 1);
    Assert(TeamNameMatcher.FindBestMatch("Manchester City", ["Manchester United"]) is null);
    Assert(TeamNameMatcher.FindBestMatch("FC Juarez", ["Juarez U21"]) is null);
}

static void BotCWeightedStatisticsAreDeterministic()
{
    var values = new[] { 10d, 8d, 6d };
    var expected = (10d + 8d * 0.5d + 6d * 0.25d) / 1.75d;
    AssertClose(BotCStatistics.WeightedAverage(values, 0.5d), expected);
    var stats = BotCStatistics.Describe(new[] { 1d, 2d, 3d, 4d }, 0.85d);
    AssertClose(stats.Median, 2.5d);
    AssertClose(stats.Variance, 1.25d);
    AssertClose(stats.StandardDeviation, Math.Sqrt(1.25d));
    AssertClose(stats.Percentile25, 1.75d);
    AssertClose(stats.Percentile75, 3.25d);
    AssertClose(stats.InterquartileRange, 1.5d);
    AssertClose(stats.MedianAbsoluteDeviation, 1d);
}

static void BotCShrinkageAndHitRatesAreCorrect()
{
    AssertClose(BotCStatistics.Shrink(12d, 10, 8d, 10d), 10d);
    AssertClose(BotCStatistics.HitRate(new[] { 8d, 9d, 10d, 11d }, 9.5d, "Under"), 0.5d);
    AssertClose(BotCStatistics.ShrunkHitRate(new[] { 8d, 9d, 10d, 11d }, 9.5d, "Under", 0.6d, 10d), 8d / 14d);
}

static void BotCPreventsTemporalLeakage()
{
    var asOf = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);
    var clean = BotCInput(asOf);
    var futureOutlier = new BotCHistoricalObservation(asOf.AddHours(1), 1000, 1000);
    var contaminated = clean with
    {
        HomeOverall = clean.HomeOverall.Append(futureOutlier).ToArray(),
        HomeVenue = clean.HomeVenue.Append(futureOutlier).ToArray(),
        AwayOverall = clean.AwayOverall.Append(futureOutlier).ToArray(),
        AwayVenue = clean.AwayVenue.Append(futureOutlier).ToArray()
    };
    var engine = new BotCPickDecisionEngine();
    var expected = engine.Evaluate(clean, new BotCStrategyConfiguration());
    var actual = engine.Evaluate(contaminated, new BotCStrategyConfiguration());
    AssertClose(actual.ContextExpectedValue, expected.ContextExpectedValue);
    AssertClose(actual.FinalEdge, expected.FinalEdge);
    AssertClose(actual.RuleBasedConfidenceScore, expected.RuleBasedConfidenceScore);
    Assert(actual.Decision == expected.Decision);
}

static void BotCApprovesSupportedCandidate()
{
    var decision = new BotCPickDecisionEngine().Evaluate(
        BotCInput(new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc)),
        new BotCStrategyConfiguration());
    Assert(decision.Decision == "Approved");
    Assert(decision.SelectedSide == "Under");
    Assert(decision.DecisionEngineType == "RuleBasedFallback");
    Assert(decision.FinalEdge > 0);
    Assert(decision.FinalExpectedValue > 0);
    Assert(decision.CombinedExactLineShrunkHitRate > 0.5d);
    Assert(decision.FeatureSnapshotJson.Contains("strictBeforeAsOf", StringComparison.Ordinal));
    Assert(decision.DecisionReasons.Contains(BotCDecisionCodes.ApprovedContext));
}

static void BotCMissingHistoryIsPending()
{
    var input = BotCInput(DateTime.UtcNow) with { AwayOverall = [], AwayVenue = [] };
    var decision = new BotCPickDecisionEngine().Evaluate(input, new BotCStrategyConfiguration());
    Assert(decision.Decision == "PendingData");
    Assert(decision.DecisionReasons.Contains(BotCDecisionCodes.PendingHistory));
}

static void BotCSupportsEveryMarket()
{
    foreach (var market in SupportedMarkets())
    {
        Assert(BotCMarketDefinition.Parse(market).Key == market);
    }
}

static void BotCDecisionIsReproducible()
{
    var input = BotCInput(new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc));
    var config = new BotCStrategyConfiguration();
    var engine = new BotCPickDecisionEngine();
    var first = engine.Evaluate(input, config);
    var second = engine.Evaluate(input, config);
    Assert(first.Decision == second.Decision);
    AssertClose(first.FinalEdge, second.FinalEdge);
    AssertClose(first.RuleBasedConfidenceScore, second.RuleBasedConfidenceScore);
    Assert(first.FeatureSnapshotJson == second.FeatureSnapshotJson);
    Assert(first.DecisionReasons.SequenceEqual(second.DecisionReasons));
}

static void BotCRejectsInvalidWeights()
{
    try
    {
        BotCStrategyConfiguration.Validate(new BotCStrategyConfiguration { WeightEdge = 0.50d });
        Assert(false);
    }
    catch (ArgumentException)
    {
    }
}

static void BotCUsesMetaModel()
{
    var predictor = new FakeBotCMetaModelPredictor(
        new BotCMetaModelPrediction(true, 0.80d, "LogisticRegression", "meta-test-v1"));
    var decision = new BotCPickDecisionEngine(predictor).Evaluate(
        BotCInput(new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc)),
        new BotCStrategyConfiguration());

    Assert(decision.DecisionEngineType == "MetaModel");
    AssertClose(decision.FinalProbability, 0.80d);
    Assert(decision.DecisionReasons.Contains(BotCDecisionCodes.ApprovedMetaProbability));
    Assert(!decision.RiskFlags.Contains(BotCRiskFlags.RuleBasedFallback));
    Assert(predictor.LastInput?.FeatureSchemaVersion == "bot-c-features-1.0.0");
    Assert(predictor.LastInput?.NumericFeatures.ContainsKey("combinedExactLineShrunkHitRate") == true);
}

static void BotCRejectsSchemaMismatchWithoutFallback()
{
    var predictor = new FakeBotCMetaModelPredictor(
        BotCMetaModelPrediction.Unavailable("Feature schema mismatch."));
    var decision = new BotCPickDecisionEngine(predictor).Evaluate(
        BotCInput(new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc)),
        new BotCStrategyConfiguration { AllowRuleBasedFallback = false });

    Assert(decision.Decision == "Rejected");
    Assert(decision.DecisionReasons.Contains(BotCDecisionCodes.RejectedModelSchemaMismatch));
    Assert(decision.RiskFlags.Contains(BotCRiskFlags.MetaModelSchemaMismatch));
}

static void BotCResolvesMarketThresholds()
{
    var configuration = new BotCStrategyConfiguration
    {
        MarketThresholds = new Dictionary<string, BotCMarketThresholdConfiguration>
        {
            ["*"] = new() { MinimumFinalEdge = 0.05d },
            ["TotalCorners"] = new() { MinimumFinalEdge = 0.07d },
            ["TotalCorners:Under"] = new() { MinimumFinalEdge = 0.09d, MinimumOdds = 1.70d }
        }
    };

    var exact = configuration.ResolveThresholds("TotalCorners", "Under");
    var market = configuration.ResolveThresholds("TotalCorners", "Over");
    var fallback = configuration.ResolveThresholds("TotalGoals", "Over");
    AssertClose(exact.MinimumFinalEdge, 0.09d);
    AssertClose(exact.MinimumOdds, 1.70d);
    AssertClose(market.MinimumFinalEdge, 0.07d);
    AssertClose(fallback.MinimumFinalEdge, 0.05d);
}

static void BotCResolvesCalibrationProfile()
{
    var configuration = new BotCStrategyConfiguration
    {
        CalibrationProfiles = new Dictionary<string, BotCCalibrationProfile>
        {
            ["*"] = new() { ModelVersion = "global-v1", Intercept = 0.10d, Slope = 0.90d, TrainingSampleCount = 500 },
            ["TotalCorners:Under"] = new() { ModelVersion = "corners-under-v1", Intercept = -0.20d, Slope = 1.10d, TrainingSampleCount = 200 }
        }
    };

    Assert(configuration.ResolveCalibration("TotalCorners", "Under").ModelVersion == "corners-under-v1");
    Assert(configuration.ResolveCalibration("TotalGoals", "Over").ModelVersion == "global-v1");
}

static void BotCRejectsBaseModelTemporalLeakage()
{
    var asOf = new DateTime(2026, 7, 15, 20, 0, 0, DateTimeKind.Utc);
    var input = BotCInput(asOf) with
    {
        BaseModelTrainedThroughUtc = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc)
    };
    var decision = new BotCPickDecisionEngine().Evaluate(input, new BotCStrategyConfiguration());
    Assert(decision.Decision == "Invalid");
    Assert(decision.DecisionReasons.Contains(BotCDecisionCodes.RejectedBaseModelTemporalLeakage));
    Assert(decision.RiskFlags.Contains(BotCRiskFlags.BaseModelTemporalLeakage));
}

static void BotDUsesCommonOpponents()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var result = BotDTeamStrengthCalculator.Calculate(
        "Alpha FC",
        "Beta FC",
        asOf,
        BotDHistory(asOf),
        BotDConfiguration());

    Assert(result.IsAvailable);
    Assert(result.CommonOpponents >= 1);
    Assert(result.DirectMatches == 0);
    Assert(result.CommonOpponentSignal > 0);
    Assert(result.AdjustedStrengthGap > 0);
    Assert(result.HomeElo > result.AwayElo);
}

static void BotDPreventsTeamStrengthLeakage()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var configuration = BotDConfiguration();
    var expected = BotDTeamStrengthCalculator.Calculate(
        "Alpha FC", "Beta FC", asOf, BotDHistory(asOf), configuration);
    var contaminated = BotDHistory(asOf)
        .Append(new BotDTeamResultObservation(
            999,
            asOf.AddMinutes(1),
            "Beta FC",
            "Alpha FC",
            20,
            0))
        .ToArray();
    var actual = BotDTeamStrengthCalculator.Calculate(
        "Alpha FC", "Beta FC", asOf, contaminated, configuration);

    AssertClose(actual.AdjustedStrengthGap, expected.AdjustedStrengthGap);
    Assert(actual.AcceptedMatches == expected.AcceptedMatches);
}

static void BotDAdjustsHomeTeamProbability()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var input = BotCInput(asOf) with
    {
        MarketType = "HomeTeamCorners",
        Line = 3.5m,
        BasePredictedValue = 4.5d,
        HomeTeam = "Alpha FC",
        AwayTeam = "Beta FC",
        TeamStrengthHistory = BotDHistory(asOf)
    };
    var engine = new BotCPickDecisionEngine();
    var baseline = engine.Evaluate(input, new BotCStrategyConfiguration());
    var botD = engine.Evaluate(input, BotDSelectorConfiguration());

    Assert(botD.SelectedSide == "Over");
    Assert(botD.FinalProbability > baseline.FinalProbability);
    Assert(botD.DecisionReasons.Contains(BotCDecisionCodes.ApprovedTeamStrength));
    Assert(botD.FeatureSnapshotJson.Contains("commonOpponentSignal", StringComparison.Ordinal));
}

static void BotDAdjustsAwayTeamProbability()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var input = BotCInput(asOf) with
    {
        MarketType = "AwayTeamCorners",
        Line = 3.5m,
        BasePredictedValue = 3d,
        HomeTeam = "Alpha FC",
        AwayTeam = "Beta FC",
        TeamStrengthHistory = BotDHistory(asOf)
    };
    var engine = new BotCPickDecisionEngine();
    var baseline = engine.Evaluate(input, new BotCStrategyConfiguration());
    var botD = engine.Evaluate(input, BotDSelectorConfiguration());

    Assert(botD.SelectedSide == "Under");
    Assert(botD.FinalProbability > baseline.FinalProbability);
}

static void BotDManifestIsExhaustive()
{
    var manifest = BotCStrategyCatalog.Build(BotDSelectorConfiguration().ToJson());
    Assert(manifest.StrategyName.Contains("Team Strength Gap", StringComparison.Ordinal));
    Assert(manifest.FeatureGroups.ContainsKey("Brecha de nivel · Bot D"));
    Assert(manifest.DataLeakageGuards.Any(item => item.Contains("red de resultados", StringComparison.Ordinal)));
}

static void BotEPreventsTemporalLeakage()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = new[]
    {
        BotEObservation(1, 101, asOf.AddHours(-9), line: 8.5m, actual: 10),
        BotEObservation(2, 102, asOf.AddHours(-8), line: 8.5m, actual: 0),
        BotEObservation(3, 103, asOf.AddMinutes(1), line: 8.5m, actual: 0)
    };

    var result = EvaluateBotE(
        asOf,
        observations,
        BotETestConfiguration(minimumObservations: 1, minimumExactMarketObservations: 1));

    Assert(result.IsAvailable);
    Assert(result.InputRows == 3);
    Assert(result.TemporallyAcceptedRows == 1);
    Assert(result.ExactMarketRows == 1);
    Assert(result.ExactMarketFixtures == 1);
    Assert(result.SelectedFixtures == 1);
    Assert(result.OutcomeCounts.GetValueOrDefault("Win") == 1);
}

static void BotEDeduplicatesFixtureEvidence()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var closestForFirstFixture = BotEObservation(
        10, 201, asOf.AddDays(-2), line: 8.5m, actual: 10, sourceProbability: 0.61d);
    var fartherDuplicateLine = BotEObservation(
        11, 201, asOf.AddDays(-2), line: 10.5m, actual: 10, sourceProbability: 0.80d);
    var secondFixture = BotEObservation(
        12, 202, asOf.AddDays(-1), line: 8.5m, actual: 10, sourceProbability: 0.63d);
    var configuration = BotETestConfiguration(
        minimumObservations: 1,
        minimumExactMarketObservations: 1);

    var withDuplicate = EvaluateBotE(
        asOf,
        [closestForFirstFixture, fartherDuplicateLine, secondFixture],
        configuration,
        sourceProbability: 0.62d);
    var deduplicated = EvaluateBotE(
        asOf,
        [closestForFirstFixture, secondFixture],
        configuration,
        sourceProbability: 0.62d);

    Assert(withDuplicate.IsAvailable);
    Assert(withDuplicate.ExactMarketRows == 3);
    Assert(withDuplicate.ExactMarketFixtures == 2);
    Assert(withDuplicate.SelectedFixtures == 2);
    Assert(withDuplicate.EffectiveSampleSize <= 2d + 0.000001d);
    Assert(withDuplicate.EvidenceHash == deduplicated.EvidenceHash);
    AssertClose(withDuplicate.WeightedAsianReturn, deduplicated.WeightedAsianReturn);
    AssertClose(withDuplicate.ConservativeEquivalentProbability, deduplicated.ConservativeEquivalentProbability);
}

static void BotEPreservesAsianQuarterReturns()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var configuration = BotETestConfiguration(
        minimumObservations: 1,
        minimumExactMarketObservations: 1);
    var halfWin = EvaluateBotE(
        asOf,
        [BotEObservation(
            20,
            301,
            asOf.AddDays(-1),
            marketType: "TotalGoals",
            side: "Under",
            line: 3.25m,
            odds: 2.20m,
            actual: 3)],
        configuration,
        marketType: "TotalGoals",
        side: "Under",
        selectedOdds: 1.80m,
        marketNoVigProbability: 1.40d / 1.80d);
    var halfLoss = EvaluateBotE(
        asOf,
        [BotEObservation(
            21,
            302,
            asOf.AddDays(-1),
            marketType: "TotalGoals",
            side: "Under",
            line: 3.75m,
            odds: 2.20m,
            actual: 4)],
        configuration,
        marketType: "TotalGoals",
        side: "Under",
        selectedOdds: 1.80m,
        marketNoVigProbability: 0.50d / 1.80d);

    Assert(halfWin.IsAvailable && halfLoss.IsAvailable);
    AssertClose(halfWin.WeightedAsianReturn, 0.40d);
    AssertClose(halfWin.PosteriorExpectedValue, 0.40d);
    AssertClose(halfWin.ConservativeExpectedValue, 0.40d);
    AssertClose(halfWin.ConservativeEquivalentProbability, 1.40d / 1.80d);
    Assert(halfWin.OutcomeCounts.GetValueOrDefault("HalfWin") == 1);
    AssertClose(halfLoss.WeightedAsianReturn, -0.50d);
    AssertClose(halfLoss.PosteriorExpectedValue, -0.50d);
    AssertClose(halfLoss.ConservativeExpectedValue, -0.50d);
    AssertClose(halfLoss.ConservativeEquivalentProbability, 0.50d / 1.80d);
    Assert(halfLoss.OutcomeCounts.GetValueOrDefault("HalfLoss") == 1);
}

static void BotERejectsInsufficientEvidence()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = new[]
    {
        BotEObservation(30, 401, asOf.AddDays(-1), line: 8.5m, actual: 10),
        BotEObservation(31, 401, asOf.AddDays(-1), line: 9.5m, actual: 10)
    };

    var result = EvaluateBotE(
        asOf,
        observations,
        BotETestConfiguration(minimumObservations: 2, minimumExactMarketObservations: 1));

    Assert(!result.IsAvailable);
    Assert(result.GlobalRows == 2);
    Assert(result.GlobalFixtures == 1);
    Assert(result.SelectedFixtures == 0);
    Assert(result.RiskFlags.SequenceEqual(["InsufficientCalibrationHistory"]));
}

static void BotERejectsCollapsedEffectiveSample()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = Enumerable.Range(1, 20)
        .Select(index => BotEObservation(
            200 + index,
            600 + index,
            asOf.AddDays(-index),
            sourceProbability: index == 1 ? 0.62d : 0.99d))
        .ToArray();
    var configuration = BotETestConfiguration(
        minimumObservations: 20,
        minimumExactMarketObservations: 12,
        minimumEffectiveObservations: 8,
        probabilityBandwidth: 0.01d);

    var result = EvaluateBotE(asOf, observations, configuration, sourceProbability: 0.62d);

    Assert(!result.IsAvailable);
    Assert(result.GlobalFixtures == 20);
    Assert(result.RiskFlags.SequenceEqual(["InsufficientEffectiveCalibrationHistory"]));
}

static void BotETargetEvidenceControlsReliability()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = Enumerable.Range(1, 20)
        .Select(index => BotEObservation(
            300 + index,
            700 + index,
            asOf.AddDays(-index),
            actual: index % 2 == 0 ? 10 : 7))
        .ToArray();
    var lowTarget = EvaluateBotE(
        asOf,
        observations,
        BotETestConfiguration(1, 1, targetEffectiveObservations: 10));
    var highTarget = EvaluateBotE(
        asOf,
        observations,
        BotETestConfiguration(1, 1, targetEffectiveObservations: 100));

    Assert(lowTarget.IsAvailable && highTarget.IsAvailable);
    Assert(lowTarget.Reliability > highTarget.Reliability);
}

static void BotEUncertaintyDoesNotCollapse()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = Enumerable.Range(1, 8)
        .Select(index => BotEObservation(
            400 + index,
            800 + index,
            asOf.AddDays(-index),
            actual: 12))
        .ToArray();
    var result = EvaluateBotE(
        asOf,
        observations,
        BotETestConfiguration(
            1,
            1,
            globalPriorStrength: 40,
            familyPriorStrength: 40,
            exactMarketPriorStrength: 40,
            confidenceZScore: 0.50d));

    Assert(result.IsAvailable);
    Assert(result.StandardError > 0.05d);
    Assert(result.ConservativeExpectedValue < result.PosteriorExpectedValue);
}

static void BotEEvidenceHashTracksCorrections()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var original = new[]
    {
        BotEObservation(500, 900, asOf.AddDays(-2), actual: 10),
        BotEObservation(501, 901, asOf.AddDays(-1), actual: 7)
    };
    var corrected = original.ToArray();
    corrected[0] = corrected[0] with { ActualValue = 6 };
    var configuration = BotETestConfiguration(1, 1);
    var before = EvaluateBotE(asOf, original, configuration);
    var after = EvaluateBotE(asOf, corrected, configuration);

    Assert(before.IsAvailable && after.IsAvailable);
    Assert(before.EvidenceHash != after.EvidenceHash);
}

static void BotECalibrationIsDeterministic()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var observations = new[]
    {
        BotEObservation(40, 501, asOf.AddDays(-4), line: 8.5m, actual: 10, sourceProbability: 0.58d),
        BotEObservation(40, 999, asOf.AddDays(-4), line: 8.5m, actual: 0, sourceProbability: 0.58d),
        BotEObservation(41, 502, asOf.AddDays(-3), line: 9.5m, actual: 8, sourceProbability: 0.63d),
        BotEObservation(42, 503, asOf.AddDays(-2), line: 9m, actual: 9, sourceProbability: 0.60d),
        BotEObservation(43, 504, asOf.AddDays(-1), line: 9.25m, actual: 10, sourceProbability: 0.67d)
    };
    var configuration = BotETestConfiguration(
        minimumObservations: 2,
        minimumExactMarketObservations: 2,
        globalPriorStrength: 3d,
        familyPriorStrength: 2d,
        exactMarketPriorStrength: 1d,
        confidenceZScore: 0.50d);

    var chronological = EvaluateBotE(asOf, observations, configuration, sourceProbability: 0.62d);
    var reversed = EvaluateBotE(asOf, observations.Reverse().ToArray(), configuration, sourceProbability: 0.62d);

    Assert(chronological.IsAvailable && reversed.IsAvailable);
    Assert(chronological.EvidenceTier == reversed.EvidenceTier);
    Assert(chronological.EvidenceHash == reversed.EvidenceHash);
    AssertClose(chronological.EffectiveSampleSize, reversed.EffectiveSampleSize);
    AssertClose(chronological.WeightedAsianReturn, reversed.WeightedAsianReturn);
    AssertClose(chronological.PosteriorExpectedValue, reversed.PosteriorExpectedValue);
    AssertClose(chronological.ConservativeEquivalentProbability, reversed.ConservativeEquivalentProbability);
    Assert(chronological.OutcomeCounts.OrderBy(value => value.Key)
        .SequenceEqual(reversed.OutcomeCounts.OrderBy(value => value.Key)));
    Assert(chronological.RiskFlags.SequenceEqual(reversed.RiskFlags));
}

static void BotCAndDRemainUnchangedWhenBotEIsDisabled()
{
    var asOf = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
    var calibrationHistory = Enumerable.Range(1, 30)
        .Select(index => BotEObservation(
            1000 + index,
            2000 + index,
            asOf.AddDays(-index),
            side: "Under",
            line: 9.5m,
            actual: 0,
            sourceProbability: 0.95d))
        .ToArray();
    var engine = new BotCPickDecisionEngine();

    var botCConfiguration = new BotCStrategyConfiguration();
    var botCBaseline = engine.Evaluate(BotCInput(asOf), botCConfiguration);
    var botCWithCalibrationRows = engine.Evaluate(
        BotCInput(asOf) with { CalibrationHistory = calibrationHistory },
        botCConfiguration);
    Assert(botCConfiguration.EmpiricalCalibration.Enabled == false);
    AssertSameDecisionValues(botCBaseline, botCWithCalibrationRows);

    var botDInput = BotCInput(asOf) with
    {
        MarketType = "HomeTeamCorners",
        Line = 3.5m,
        BasePredictedValue = 4.5d,
        HomeTeam = "Alpha FC",
        AwayTeam = "Beta FC",
        TeamStrengthHistory = BotDHistory(asOf)
    };
    var botDConfiguration = BotDSelectorConfiguration();
    var botDBaseline = engine.Evaluate(botDInput, botDConfiguration);
    var botDWithCalibrationRows = engine.Evaluate(
        botDInput with { CalibrationHistory = calibrationHistory },
        botDConfiguration);
    Assert(botDConfiguration.EmpiricalCalibration.Enabled == false);
    AssertSameDecisionValues(botDBaseline, botDWithCalibrationRows);
}

static void BotFLegacySourceRequiresProvenance()
{
    try
    {
        BotCStrategyConfiguration.Validate(new BotCStrategyConfiguration
        {
            BasePredictionSource = "LEGACY"
        });
        throw new Exception("Expected missing legacy provenance to be rejected.");
    }
    catch (ArgumentException exception)
    {
        Assert(exception.Message.Contains("BaseModelVersionOverride", StringComparison.Ordinal));
    }

    var valid = BotCStrategyConfiguration.Validate(new BotCStrategyConfiguration
    {
        ConfigurationVersion = "bot-f-legacy-empirical-test",
        FeatureSchemaVersion = "bot-f-legacy-features-test",
        BasePredictionSource = "LEGACY",
        BaseModelVersionOverride = "legacy-test-v1",
        BaseModelTrainedThroughUtc = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc)
    });
    Assert(valid.BasePredictionSource == "LEGACY");
}

static void FootballIntelligenceIsNeutralWithoutEvidence()
{
    var asOf = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    var configuration = IntelligenceConfiguration();
    var cases = new MatchIntelligenceSnapshotPair?[]
    {
        null,
        new(5001, IntelligenceSnapshot(1, 5001, asOf.AddHours(-1), actionableFacts: 0), null),
        new(5002, IntelligenceSnapshot(2, 5002, asOf.AddHours(-1), confidence: 0.20m), null),
        new(5003, IntelligenceSnapshot(3, 5003, asOf.AddHours(1)), null),
        new(5004, IntelligenceSnapshot(4, 5004, asOf.AddHours(-1), ageMinutes: 5000), null)
    };

    foreach (var snapshots in cases)
    {
        var result = FootballIntelligenceAdjustmentCalculator.Calculate(
            asOf,
            "HomeTeamGoals",
            "Over",
            0.61d,
            snapshots,
            configuration);
        Assert(!result.IsApplied);
        AssertClose(result.ProbabilityAdjustment, 0d);
        AssertClose(result.ProbabilityBefore, 0.61d);
        AssertClose(result.ProbabilityAfter, 0.61d);
    }
}

static void FootballIntelligenceAppliesBoundedMarketSignal()
{
    var asOf = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    var snapshots = new MatchIntelligenceSnapshotPair(
        6001,
        IntelligenceSnapshot(
            10,
            6001,
            asOf.AddMinutes(-5),
            attackImpact: 0.50m,
            goalImpact: 0.30m,
            missingGoalShare: 0.20m),
        null);
    var over = FootballIntelligenceAdjustmentCalculator.Calculate(
        asOf, "HomeTeamGoals", "Over", 0.60d, snapshots, IntelligenceConfiguration());
    var under = FootballIntelligenceAdjustmentCalculator.Calculate(
        asOf, "HomeTeamGoals", "Under", 0.60d, snapshots, IntelligenceConfiguration());

    Assert(over.IsApplied && under.IsApplied);
    Assert(over.ProbabilityAdjustment < 0d);
    Assert(under.ProbabilityAdjustment > 0d);
    Assert(Math.Abs(over.ProbabilityAdjustment) <= 0.0400000001d);
    Assert(Math.Abs(under.ProbabilityAdjustment) <= 0.0400000001d);
}

static void FootballIntelligenceReportsEffectiveClampedAdjustment()
{
    var asOf = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    var snapshots = new MatchIntelligenceSnapshotPair(
        6002,
        IntelligenceSnapshot(
            11,
            6002,
            asOf.AddMinutes(-5),
            attackImpact: 0.90m,
            goalImpact: 0.90m,
            missingGoalShare: 0.90m),
        null);
    var result = FootballIntelligenceAdjustmentCalculator.Calculate(
        asOf,
        "HomeTeamGoals",
        "Under",
        0.99d,
        snapshots,
        IntelligenceConfiguration());

    Assert(result.IsApplied);
    AssertClose(result.ProbabilityAfter, 1d);
    AssertClose(result.ProbabilityAdjustment, 0.01d);
    AssertClose(result.ProbabilityAdjustment, result.ProbabilityAfter - result.ProbabilityBefore);
}

static void ActiveBotsEnableFootballIntelligence()
{
    var schema = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "CornersPredictionApi", "sql", "automated_corners_bot.sql"));
    Assert(schema.Contains(
        "BotKey IN (N'A', N'C2026', N'D2026', N'E2026', N'F2026', N'G2026', N'H2026')",
        StringComparison.Ordinal));
    Assert(schema.Contains("'$.footballIntelligence'", StringComparison.Ordinal));
    Assert(schema.Contains("N'bot-a-intelligence-1.1.0'", StringComparison.Ordinal));
    Assert(schema.Contains("N'bot-c-intelligence-1.1.0'", StringComparison.Ordinal));
    Assert(schema.Contains("N'bot-d-strength-intelligence-1.1.0'", StringComparison.Ordinal));
    Assert(schema.Contains("N'bot-g-goals-market-intelligence-1.1.0'", StringComparison.Ordinal));
    Assert(schema.Contains("N'bot-h-corners-calibration-shadow-1.1.0-intelligence'", StringComparison.Ordinal));
    Assert(RecommendationBotLifecycle.IsRetired("B"));
}

static void BotEAndFRemainNumericallyUnchangedWithoutInternetEvidence()
{
    var asOf = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
    var history = Enumerable.Range(1, 24)
        .Select(index => BotEObservation(
            4000 + index,
            7000 + index,
            asOf.AddDays(-index),
            line: 9.5m,
            actual: index % 2 == 0 ? 10 : 8,
            sourceProbability: 0.58d))
        .ToArray();
    var input = BotCInput(asOf) with { CalibrationHistory = history };
    var calibration = BotETestConfiguration(2, 2, minimumEffectiveObservations: 1);
    var eBaseline = new BotCStrategyConfiguration { EmpiricalCalibration = calibration };
    var eIntelligence = eBaseline with { FootballIntelligence = IntelligenceConfiguration() };
    var fBaseline = eBaseline with
    {
        BasePredictionSource = "LEGACY",
        BaseModelVersionOverride = "legacy-test-v1",
        BaseModelTrainedThroughUtc = asOf.AddYears(-1)
    };
    var fIntelligence = fBaseline with { FootballIntelligence = IntelligenceConfiguration() };
    var engine = new BotCPickDecisionEngine();

    AssertNeutralEngineDecision(engine.Evaluate(input, eBaseline), engine.Evaluate(input, eIntelligence));
    AssertNeutralEngineDecision(engine.Evaluate(input, fBaseline), engine.Evaluate(input, fIntelligence));
}

static void NewsExtractionRequiresExplicitEvidence()
{
    var extractor = new RuleBasedNewsFactExtractor();
    var request = new NewsExtractionRequest(
        8001,
        new DateTime(2026, 8, 19, 20, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc),
        "Alpha FC",
        "Beta FC",
        "Team news",
        new DateTime(2026, 8, 18, 10, 0, 0, DateTimeKind.Utc),
        "Alex Uno returned to training. Alex Dos has been ruled out. Alex Tres is not injured.",
        "en",
        ["Alex Uno", "Alex Dos", "Alex Tres"]);
    var result = extractor.ExtractAsync(request, CancellationToken.None).GetAwaiter().GetResult();

    Assert(result.Facts.Count == 2);
    Assert(result.Facts.Single(value => value.PlayerName == "Alex Uno").AvailabilityStatus == AvailabilityStatus.Unknown);
    Assert(result.Facts.Single(value => value.PlayerName == "Alex Dos").AvailabilityStatus == AvailabilityStatus.ConfirmedOut);
    Assert(result.Facts.All(value => value.PlayerName != "Alex Tres"));
}

static void NewsQueriesAreLanguageBalanced()
{
    var queries = new FootballNewsQueryBuilder()
        .Build("Alpha FC", "Beta FC", languages: ["en", "es", "pt"])
        .Take(6)
        .ToArray();
    Assert(queries.Any(value => value.Contains("injury update", StringComparison.Ordinal)));
    Assert(queries.Any(value => value.Contains("lesionados", StringComparison.Ordinal)));
    Assert(queries.Any(value => value.Contains("desfalques", StringComparison.Ordinal)));
}

static void NewsFactsConsolidateDeterministically()
{
    var cutoff = new DateTime(2026, 8, 18, 18, 0, 0, DateTimeKind.Utc);
    var injury = IntelligenceFact(
        1,
        101,
        "Alex Uno",
        FootballNewsEventType.Injury,
        AvailabilityStatus.ConfirmedOut,
        cutoff.AddHours(-3));
    var duplicateName = injury with { Id = 2, PlayerNameExtracted = "A. Uno", FirstSeenAtUtc = cutoff.AddHours(-2) };
    var officialStarter = IntelligenceFact(
        3,
        101,
        "Alex Uno",
        FootballNewsEventType.OfficialStarter,
        AvailabilityStatus.Starting,
        cutoff.AddMinutes(-30));
    var consolidated = new NewsFactConsolidator().Consolidate(
        [injury, duplicateName, officialStarter],
        cutoff);

    Assert(consolidated.Count == 3);
    Assert(consolidated.Count(value => value.IsCurrent) == 1);
    Assert(consolidated.Single(value => value.IsCurrent).EventType == FootballNewsEventType.OfficialStarter);
}

static FootballNewsFact IntelligenceFact(
    long id,
    int playerId,
    string playerName,
    FootballNewsEventType eventType,
    AvailabilityStatus status,
    DateTime observedAtUtc) => new()
{
    Id = id,
    NewsDocumentId = id,
    FactHash = $"test-{id}",
    FixtureId = 9001,
    TeamId = 100,
    PlayerId = playerId,
    TeamNameExtracted = "Alpha FC",
    PlayerNameExtracted = playerName,
    EventType = eventType,
    AvailabilityStatus = status,
    Certainty = FactCertainty.Confirmed,
    ProbabilityAvailable = status == AvailabilityStatus.Starting ? 1m : 0m,
    EvidenceSnippet = eventType.ToString(),
    FixtureRelevance = 1m,
    ExtractionConfidence = 1m,
    SourceConfidence = 1m,
    EffectiveConfidence = 1m,
    ResolutionStatus = EntityResolutionStatus.ResolvedExact,
    IsCurrent = true,
    ExtractionModel = "test",
    PromptVersion = "test-v1",
    IsCurrentExtraction = true,
    FirstSeenAtUtc = observedAtUtc,
    EventEffectiveAtUtc = observedAtUtc,
    CreatedAtUtc = observedAtUtc
};

static void AssertNeutralEngineDecision(BotCPickDecision baseline, BotCPickDecision withIntelligence)
{
    Assert(baseline.Decision == withIntelligence.Decision);
    Assert(baseline.SelectedSide == withIntelligence.SelectedSide);
    AssertClose(baseline.FinalProbability, withIntelligence.FinalProbability);
    AssertClose(baseline.FinalEdge, withIntelligence.FinalEdge);
    AssertClose(baseline.FinalExpectedValue, withIntelligence.FinalExpectedValue);
    AssertClose(baseline.SelectionScore, withIntelligence.SelectionScore);
    Assert(withIntelligence.DecisionReasons.Contains(BotCDecisionCodes.NeutralFootballIntelligence));
}

static FootballIntelligenceAdjustmentConfiguration IntelligenceConfiguration() => new()
{
    Enabled = true,
    Weight = 0.35d,
    MaximumProbabilityAdjustment = 0.04d,
    MinimumTeamConfidence = 0.60d,
    MaximumSnapshotAgeMinutes = 4320,
    MinimumActionableFacts = 1,
    MinimumIndependentSources = 1
};

static MatchTeamIntelligenceSnapshot IntelligenceSnapshot(
    long id,
    long fixtureId,
    DateTime cutoffUtc,
    int actionableFacts = 1,
    decimal confidence = 0.80m,
    int ageMinutes = 10,
    decimal attackImpact = 0m,
    decimal goalImpact = 0m,
    decimal missingGoalShare = 0m) => new()
{
    Id = id,
    FixtureId = fixtureId,
    TeamId = 100,
    IsHomeTeam = true,
    CutoffAtUtc = cutoffUtc,
    KickoffAtUtc = cutoffUtc.AddHours(2),
    IndependentSourceCount = actionableFacts > 0 ? 1 : 0,
    ActionableFactCount = actionableFacts,
    OverallNewsConfidence = confidence,
    SnapshotAgeMinutes = ageMinutes,
    AttackAvailabilityImpact = attackImpact,
    GoalScoringAvailabilityImpact = goalImpact,
    MissingGoalShare = missingGoalShare,
    RiskFlagsJson = "[]",
    DetailJson = "{}"
};

static void AssertSameDecisionValues(BotCPickDecision expected, BotCPickDecision actual)
{
    Assert(expected.Decision == actual.Decision);
    Assert(expected.DecisionEngineType == actual.DecisionEngineType);
    Assert(expected.SelectedSide == actual.SelectedSide);
    Assert(expected.SelectedOdds == actual.SelectedOdds);
    AssertClose(expected.FinalProbability, actual.FinalProbability);
    AssertClose(expected.FinalEdge, actual.FinalEdge);
    AssertClose(expected.FinalExpectedValue, actual.FinalExpectedValue);
    AssertClose(expected.SelectionScore, actual.SelectionScore);
    Assert(expected.DecisionReasons.SequenceEqual(actual.DecisionReasons));
    Assert(expected.RiskFlags.SequenceEqual(actual.RiskFlags));
}

static BotEEmpiricalCalibrationResult EvaluateBotE(
    DateTime asOf,
    IReadOnlyList<BotECalibrationObservation> observations,
    BotEEmpiricalCalibrationConfiguration configuration,
    string marketType = "TotalCorners",
    string side = "Over",
    decimal selectedOdds = 1.80m,
    double sourceProbability = 0.62d,
    double marketNoVigProbability = 0.50d,
    string baseModelVersion = "test-v1") =>
    BotEEmpiricalCalibrationCalculator.Calculate(
        asOf,
        marketType,
        side,
        selectedOdds,
        baseModelVersion,
        sourceProbability,
        marketNoVigProbability,
        observations,
        configuration);

static BotEEmpiricalCalibrationConfiguration BotETestConfiguration(
    int minimumObservations,
    int minimumExactMarketObservations,
    double globalPriorStrength = 1d,
    double familyPriorStrength = 1d,
    double exactMarketPriorStrength = 1d,
    double confidenceZScore = 0d,
    int minimumEffectiveObservations = 1,
    int targetEffectiveObservations = 1,
    double probabilityBandwidth = 0.20d) =>
    new()
    {
        Enabled = true,
        MinimumObservations = minimumObservations,
        MinimumExactMarketObservations = minimumExactMarketObservations,
        MinimumEffectiveObservations = minimumEffectiveObservations,
        TargetEffectiveObservations = targetEffectiveObservations,
        OutcomeAvailabilityLagHours = 8,
        ProbabilityBandwidth = probabilityBandwidth,
        GlobalPriorStrength = globalPriorStrength,
        FamilyPriorStrength = familyPriorStrength,
        ExactMarketPriorStrength = exactMarketPriorStrength,
        RecencyHalfLifeDays = 3650d,
        QualityWeightFloor = 1d,
        MinimumReliability = 0d,
        ConfidenceZScore = confidenceZScore,
        RequireSameBaseModelVersion = true,
        RequireNoVigProbability = true
    };

static BotECalibrationObservation BotEObservation(
    long evaluationId,
    long fixtureId,
    DateTime matchDateUtc,
    string marketType = "TotalCorners",
    string side = "Over",
    decimal line = 8.5m,
    decimal odds = 1.90m,
    int actual = 10,
    double sourceProbability = 0.62d,
    double marketNoVigProbability = 0.50d,
    double dataQualityScore = 1d,
    string baseModelVersion = "test-v1") =>
    new(
        evaluationId,
        fixtureId,
        matchDateUtc,
        marketType,
        side,
        line,
        odds,
        actual,
        sourceProbability,
        marketNoVigProbability,
        dataQualityScore,
        baseModelVersion);

static BotCStrategyConfiguration BotDSelectorConfiguration() =>
    new()
    {
        ConfigurationVersion = "bot-d-strength-gap-test",
        FeatureSchemaVersion = "bot-d-features-test",
        TeamStrength = BotDConfiguration()
    };

static BotDTeamStrengthConfiguration BotDConfiguration() =>
    new()
    {
        Enabled = true,
        MinimumMatchesPerTeam = 1,
        MinimumConfidenceScore = 0d,
        MaximumProbabilityAdjustment = 0.10d
    };

static IReadOnlyList<BotDTeamResultObservation> BotDHistory(DateTime asOf) =>
[
    new(1, asOf.AddDays(-20), "Alpha FC", "Shared United", 3, 0),
    new(2, asOf.AddDays(-18), "Shared United", "Beta FC", 2, 0),
    new(3, asOf.AddDays(-12), "Alpha FC", "Gamma FC", 2, 0),
    new(4, asOf.AddDays(-10), "Delta FC", "Beta FC", 1, 0)
];

static BotCPickEvaluationInput BotCInput(DateTime asOf)
{
    var overall = Enumerable.Range(1, 20)
        .Select(index => new BotCHistoricalObservation(asOf.AddDays(-index), 4d, 4d))
        .ToArray();
    var venue = overall.Take(10).ToArray();
    return new BotCPickEvaluationInput(
        "TotalCorners",
        9.5m,
        1.90m,
        1.90m,
        asOf.AddHours(-2),
        asOf,
        8d,
        1d,
        "Models 2026",
        "test-v1",
        overall,
        venue,
        overall,
        venue);
}

static void AssertClose(double actual, double expected, double tolerance = 0.000001d)
{
    if (Math.Abs(actual - expected) > tolerance)
    {
        throw new InvalidOperationException($"Assertion failed. Expected {expected}, actual {actual}.");
    }
}

static IEnumerable<string> SupportedMarkets() =>
[
    "TotalGoals", "HomeTeamGoals", "AwayTeamGoals",
    "TotalCorners", "HomeTeamCorners", "AwayTeamCorners",
    "TotalShots", "HomeTeamShots", "AwayTeamShots",
    "TotalShotsOnGoal", "HomeTeamShotsOnGoal", "AwayTeamShotsOnGoal"
];

static AutomatedBotPickSettlementCandidate Candidate(
    string marketType,
    int? homeGoals = 1,
    int? awayGoals = 1,
    int? homeCorners = 1,
    int? awayCorners = 1,
    int? homeShots = 1,
    int? awayShots = 1,
    int? homeShotsOnGoal = 1,
    int? awayShotsOnGoal = 1) => new()
{
    SelectionId = 1,
    MarketType = marketType,
    SelectedSide = "Over",
    LineValue = 0.5m,
    Odds = 1.90m,
    Stake = 1m,
    MatchHistoryId = 1,
    MatchCandidateCount = 1,
    FixtureStatus = "FT",
    HomeGoals = homeGoals,
    AwayGoals = awayGoals,
    HomeCorners = homeCorners,
    AwayCorners = awayCorners,
    HomeShots = homeShots,
    AwayShots = awayShots,
    HomeShotsOnGoal = homeShotsOnGoal,
    AwayShotsOnGoal = awayShotsOnGoal
};

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed.");
    }
}

sealed class FakeBotCMetaModelPredictor(BotCMetaModelPrediction prediction) : IBotCMetaModelPredictor
{
    public BotCMetaModelInput? LastInput { get; private set; }

    public BotCMetaModelPrediction Predict(BotCMetaModelInput input)
    {
        LastInput = input;
        return prediction;
    }
}

sealed class FakeSettlementRepository : IAutomatedBotPickSettlementRepository
{
    private readonly List<AutomatedBotPickSettlementCandidate> _pending;

    public FakeSettlementRepository(params AutomatedBotPickSettlementCandidate[] pending)
    {
        _pending = pending.ToList();
    }

    public int ApplyCalls { get; private set; }

    public AutomatedBotPickSettlementFilter? LastFilter { get; private set; }

    public Task<IReadOnlyList<AutomatedBotPickSettlementCandidate>> GetPendingCandidatesAsync(
        AutomatedBotPickSettlementFilter filter,
        CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<IReadOnlyList<AutomatedBotPickSettlementCandidate>>(
            _pending.Take(filter.MaxRows).ToArray());
    }

    public Task<AutomatedBotPickSettlementApplyResult> ApplyAsync(
        IReadOnlyCollection<AutomatedBotPickSettlementUpdate> updates,
        CancellationToken cancellationToken)
    {
        ApplyCalls++;
        var applied = updates.Count(update => _pending.RemoveAll(candidate => candidate.SelectionId == update.SelectionId) > 0);
        var settled = updates.Count(update => update.Status != "Pending");
        return Task.FromResult(new AutomatedBotPickSettlementApplyResult(applied, settled));
    }
}
