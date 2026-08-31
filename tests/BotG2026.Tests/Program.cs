using System.Reflection;
using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation;
using CornersPrediction.Application.Automation.BotG;
using CornersPrediction.Domain.Automation.BotG;

var AsOfUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

var tests = new (string Name, Action Run)[]
{
    ("configuration defaults to isolated shadow mode", ConfigurationDefaults),
    ("configuration JSON round-trips and validates publish isolation", ConfigurationJson),
    ("bot league filters isolate market families and exclusion wins", BotLeagueFilters),
    ("bot league filter JSON normalizes and round-trips", BotLeagueFilterJson),
    ("strict no-vig uses both sides and normalizes to one", StrictNoVig),
    ("strict no-vig refuses one-sided odds", StrictNoVigUnavailable),
    ("feature builder emits stable 5/10/20 overall and venue windows", FeatureWindows),
    ("feature vector has stable market and temporal names", FeatureVectorNames),
    ("feature builder blocks future odds", FutureOddsLeakage),
    ("feature builder blocks same-time and future results", HistoryLeakage),
    ("feature builder blocks model trained-through leakage", ModelLeakage),
    ("probability line curves fail closed on monotonicity violations", ProbabilityMonotonicityGate),
    ("automation history mapping excludes same-day future and foreign-team rows", AutomationHistoryMapping),
    ("automation UTC and immutable-snapshot helpers preserve temporal meaning", AutomationTemporalHelpers),
    ("automation refuses to publish an abstained candidate", AutomationPublicationGuard),
    ("zero logit residual is exactly market neutral", MetaNeutrality),
    ("meta model reports missing feature and schema unavailability", MetaUnavailable),
    ("meta model rejects synthetic and non-deployable artifacts", MetaDeploymentGate),
    ("meta model fails closed on artifact/runtime identity mismatch", MetaCompatibilityGate),
    ("meta model rejects malformed artifact evidence at load time", MetaMalformedEvidence),
    ("meta model blocks trained-through leakage and exposes dispersion", MetaTemporalAndEnsemble),
    ("hierarchical calibration reaches exact bookmaker leaf", CalibrationHierarchy),
    ("calibration isolates family market side and bookmaker", CalibrationIsolation),
    ("calibration rejects evidence inside outcome lag", CalibrationTemporalLag),
    ("uncertainty probability and edge are conservative", ConservativeProbabilityAndEdge),
    ("expected value covers five Asian settlement states", ExpectedValueDistribution),
    ("conservative EV never exceeds nominal EV", ConservativeExpectedValue),
    ("robust OOD is centered in distribution and severe outside", RobustOod),
    ("OOD is unavailable when required evidence is missing", OodUnavailable),
    ("abstention approves a complete safe candidate", AbstentionApproved),
    ("abstention distinguishes unsafe evidence from rejected value", AbstentionAndRejection),
    ("selector ranks and emits at most one approved pick per fixture", RankingOnePerFixture),
    ("Asian quarter-goal settlement is exact for Total Home and Away", AsianSettlementMatrix),
    ("candidate audit list pages keys before loading wide snapshots", CandidateAuditQueryIsLightweight),
    ("scorecard aggregates a narrow date-indexed candidate set without fivefold expansion", ScorecardQueryIsLightweight),
    ("Bot G SQL endpoints wait for schema readiness", BotGEndpointsWaitForSchema)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
    throw new InvalidOperationException($"{failures.Count} Bot G test(s) failed.{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");

Console.WriteLine($"All {tests.Length} Bot G 2026 tests passed.");

void ConfigurationDefaults()
{
    var config = BotGConfiguration.FromJson(null);
    Check.Equal("G2026", config.BotKey);
    Check.True(config.Enabled);
    Check.True(config.ShadowMode);
    Check.False(config.PublishEnabled);
    Check.SequenceEqual(new[] { 5, 10, 20 }, config.Features.Windows);
    Check.SequenceEqual(
        new[] { BotGMarketType.TotalGoals, BotGMarketType.HomeTeamGoals, BotGMarketType.AwayTeamGoals },
        config.SupportedMarkets);
}

void CandidateAuditQueryIsLightweight()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    string? migrationPath = null;
    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "CornersPredictionApi",
            "sql",
            "20260819_bot_g2026.sql");
        if (File.Exists(candidate))
        {
            migrationPath = candidate;
            break;
        }

        current = current.Parent;
    }

    Check.True(migrationPath is not null, "Bot G migration was not found from the test output directory.");
    var migration = File.ReadAllText(migrationPath!);
    var start = migration.IndexOf(
        "CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026Candidates",
        StringComparison.Ordinal);
    var end = migration.IndexOf(
        "CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026CandidateDetail",
        start,
        StringComparison.Ordinal);
    Check.True(start >= 0 && end > start, "Bot G candidate-list procedure was not found.");

    var procedure = migration[start..end];
    Check.True(procedure.Contains("CREATE TABLE #CandidatePage", StringComparison.Ordinal));
    Check.True(procedure.Contains("OPTION (RECOMPILE)", StringComparison.Ordinal));
    Check.True(procedure.Contains("N'{}' AS FeatureSnapshotJson", StringComparison.Ordinal));
    Check.True(procedure.Contains("N'[]' AS DecisionReasonsJson", StringComparison.Ordinal));
    Check.False(procedure.Contains("CONVERT(NVARCHAR(2000), candidate.DecisionReasonsJson)", StringComparison.Ordinal),
        "The paged list must load decision JSON only through the detail endpoint.");
    Check.False(procedure.Contains("candidate.*", StringComparison.OrdinalIgnoreCase),
        "The paged list must not materialize every view column or the feature snapshot.");
    Check.True(migration.Contains(
        "IX_AutomatedBotPickEvaluations_G2026CandidateAuditV2",
        StringComparison.Ordinal));
    Check.True(migration.Contains(
        "IX_AutomatedBotPickEvaluations_G2026CandidatePageV3",
        StringComparison.Ordinal));
}

void ScorecardQueryIsLightweight()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    string? migrationPath = null;
    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "CornersPredictionApi",
            "sql",
            "20260819_bot_g2026.sql");
        if (File.Exists(candidate))
        {
            migrationPath = candidate;
            break;
        }

        current = current.Parent;
    }

    Check.True(migrationPath is not null, "Bot G migration was not found from the test output directory.");
    var migration = File.ReadAllText(migrationPath!);
    var start = migration.IndexOf(
        "CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026Scorecard",
        StringComparison.Ordinal);
    Check.True(start >= 0, "Bot G scorecard procedure was not found.");
    var procedure = migration[start..];

    Check.True(procedure.Contains("INTO #CandidateBase", StringComparison.Ordinal));
    Check.True(procedure.Contains("OPTION (RECOMPILE)", StringComparison.Ordinal));
    Check.True(procedure.Contains("GROUP BY GROUPING SETS", StringComparison.Ordinal));
    Check.True(procedure.Contains("FROM #CandidateBase", StringComparison.Ordinal));
    Check.True(procedure.Contains(
        "FROM dbo.AutomatedBotPickEvaluations AS evaluation",
        StringComparison.Ordinal));
    Check.False(procedure.Contains("candidate.*", StringComparison.OrdinalIgnoreCase),
        "The scorecard must not repeatedly materialize the wide candidate view.");
    Check.False(procedure.Contains("INTO #Expanded", StringComparison.Ordinal),
        "The scorecard must not copy every candidate once per reporting dimension.");
    Check.True(migration.Contains(
        "IX_AutomatedBotPickEvaluations_G2026ScorecardV2",
        StringComparison.Ordinal));
}

void BotGEndpointsWaitForSchema()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    string? controllerPath = null;
    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "CornersPredictionApi",
            "Controllers",
            "BotG2026Controller.cs");
        if (File.Exists(candidate))
        {
            controllerPath = candidate;
            break;
        }

        current = current.Parent;
    }

    Check.True(controllerPath is not null, "Bot G API controller was not found.");
    var controller = File.ReadAllText(controllerPath!);
    Check.True(controller.Contains(
        "SqlAutomationRepository schemaRepository",
        StringComparison.Ordinal));
    const string readinessCall =
        "await _schemaRepository.EnsureSchemaAsync(cancellationToken);";
    Check.Equal(
        5,
        controller.Split(readinessCall, StringSplitOptions.None).Length - 1,
        "Every Bot G SQL action must wait for the shared schema initializer.");
}

void ConfigurationJson()
{
    var config = BotGConfiguration.FromJson("{}") with
    {
        FootballIntelligence = new BotGFootballIntelligenceConfiguration
        {
            Enabled = true,
            Version = "football-intelligence-adjustment-test"
        }
    };
    var roundTrip = BotGConfiguration.FromJson(config.ToJson());
    Check.Equal(config.ConfigurationVersion, roundTrip.ConfigurationVersion);
    Check.Equal(BotGMarketType.HomeTeamGoals, roundTrip.SupportedMarkets[1]);
    Check.True(roundTrip.FootballIntelligence.Enabled);
    Check.Equal("football-intelligence-adjustment-test", roundTrip.FootballIntelligence.Version);
    Check.Throws<ArgumentException>(() => BotGConfiguration.FromJson("{\"publishEnabled\":true}"));
    Check.Throws<ArgumentException>(() => BotGConfiguration.Validate(config with { ShadowMode = false }));
    Check.Throws<ArgumentException>(() => BotGConfiguration.Validate(
        config with { MetaModel = config.MetaModel with { Required = false } }));

    var now = DateTime.UtcNow;
    var tableEnabledButConfigShadow = new RecommendationBotDefinitionDto(
        "G2026", "Bot G Goals Specialist", "test",
        RecommendationBotBaseStrategies.GoalsMarketAnchored,
        true, true, true, ["GOALS"], null, null, null, null, null, null, null, 1m,
        config.ToJson(), now, now);
    Check.False(tableEnabledButConfigShadow.GoalsMarketAnchoredConfiguration!.PublishEnabled,
        "The strategy-config publication gate must remain independent of the table gate.");
    Check.True(tableEnabledButConfigShadow.GoalsMarketAnchoredConfiguration.ShadowMode);
    Check.True(tableEnabledButConfigShadow.FootballIntelligenceConfiguration.Enabled);

    var configEnabledButTableShadow = tableEnabledButConfigShadow with
    {
        PublishEnabled = false,
        StrategyConfigurationJson = (config with { PublishEnabled = true, ShadowMode = false }).ToJson()
    };
    Check.True(configEnabledButTableShadow.GoalsMarketAnchoredConfiguration!.PublishEnabled);
    Check.False(configEnabledButTableShadow.PublishEnabled,
        "The table publication gate must remain independent of the strategy config.");
}

void BotLeagueFilters()
{
    var filters = new[]
    {
        new RecommendationBotLeagueFilter(
            "CORNERS",
            ["Chile - Primera Division", "England - Premier League"],
            ["Chile - *"])
    };

    Check.False(RecommendationBotLeaguePolicy.IsAllowed(
        filters, "CORNERS", "Chile - Primera Division"),
        "An exclusion must win even when the same league is included.");
    Check.False(RecommendationBotLeaguePolicy.IsAllowed(filters, "CORNERS", "Chile - Primera B"));
    Check.True(RecommendationBotLeaguePolicy.IsAllowed(filters, "CORNERS", "England - Premier League"));
    Check.False(RecommendationBotLeaguePolicy.IsAllowed(filters, "CORNERS", "Spain - La Liga"));
    Check.True(RecommendationBotLeaguePolicy.IsAllowed(
        filters, "GOALS", "Chile - Primera Division"),
        "A CORNERS rule must not suppress GOALS for the same bot.");
    Check.True(RecommendationBotLeaguePolicy.IsAllowed([], "CORNERS", "Chile - Primera Division"));
}

void BotLeagueFilterJson()
{
    var json = RecommendationBotLeaguePolicy.ToJson(
    [
        new RecommendationBotLeagueFilter(
            " corners ",
            [" England  -  Premier League ", "England - Premier League"],
            [" Chile -  * "])
    ]);
    var restored = RecommendationBotLeaguePolicy.FromJson(json);
    Check.Equal(1, restored.Count);
    Check.Equal("CORNERS", restored[0].MarketFamily);
    Check.SequenceEqual(new[] { "England - Premier League" }, restored[0].IncludedLeagues);
    Check.SequenceEqual(new[] { "Chile - *" }, restored[0].ExcludedLeagues);
}

void StrictNoVig()
{
    var service = new StrictMarketProbabilityService();
    var symmetric = service.Calculate(Quote(overOdds: 2m, underOdds: 2m));
    Check.True(symmetric.IsAvailable);
    Check.Close(0.5d, symmetric.SelectedNoVigProbability);
    Check.Close(1d, symmetric.NoVigOver + symmetric.NoVigUnder);

    var asymmetric = service.Calculate(Quote(selection: BotGSelection.Under, overOdds: 1.80m, underOdds: 2.20m));
    Check.Close(1d, asymmetric.NoVigOver + asymmetric.NoVigUnder);
    Check.Close(asymmetric.NoVigUnder, asymmetric.SelectedNoVigProbability);
    Check.True(asymmetric.SelectedNoVigProbability < asymmetric.NoVigOver);
}

void StrictNoVigUnavailable()
{
    var service = new StrictMarketProbabilityService();
    Check.False(service.Calculate(Quote(underOdds: null)).IsAvailable);
    Check.False(service.Calculate(Quote(overOdds: 1m)).IsAvailable);
}

void FeatureWindows()
{
    var features = BuildFeatures();
    Check.Equal(5, features.Overall.Last5.SampleCount);
    Check.Equal(10, features.Overall.Last10.SampleCount);
    Check.Equal(20, features.Overall.Last20.SampleCount);
    Check.Equal(20, features.Venue.Last20.SampleCount);
    Check.Close(3d, features.Overall.Last5.Mean);
    Check.Close(5.5d, features.Overall.Last10.Mean);
    Check.Close(10.5d, features.Overall.Last20.Mean);
    Check.True(features.AsOfDateUtc == AsOfUtc);
    Check.True(features.HistoryCount >= 20);
}

void FeatureVectorNames()
{
    var vector = BuildFeatures().ToNumericVector();
    foreach (var name in new[]
    {
        "marketNoVigProbability", "legacyPrediction", "prediction2026",
        "overallLast5WeightedMean", "overallLast10Mean", "overallLast20Mad",
        "venueLast5Mean", "venueLast10WeightedMean", "venueLast20Iqr"
    })
        Check.True(vector.ContainsKey(name), $"Missing stable feature {name}.");
    Check.True(vector.Values.All(double.IsFinite));
}

void FutureOddsLeakage()
{
    var quote = Quote() with { OddsTimestampUtc = AsOfUtc.AddSeconds(1) };
    Check.Throws<BotGTemporalLeakageException>(() => BuildFeatures(quote));
}

void HistoryLeakage()
{
    var input = FeatureInput(Quote());
    var leaked = input.HomeOverall.Concat(
        [new BotGHistoryObservation(999_999, AsOfUtc, 1d, 0d)]).ToArray();
    Check.Throws<BotGTemporalLeakageException>(() =>
        new BotGFeatureBuilder().Build(input with { HomeOverall = leaked }, new BotGConfiguration()));
}

void ModelLeakage()
{
    var input = FeatureInput(Quote());
    Check.Throws<BotGTemporalLeakageException>(() => new BotGFeatureBuilder().Build(
        input with { Predictions = input.Predictions with { Model2026TrainedThroughUtc = AsOfUtc } },
        new BotGConfiguration()));
    Check.Throws<BotGTemporalLeakageException>(() => new BotGFeatureBuilder().Build(
        input with { Predictions = input.Predictions with { LegacyTrainedThroughUtc = null } },
        new BotGConfiguration()));
    Check.Throws<BotGTemporalLeakageException>(() => new BotGFeatureBuilder().Build(
        input with { Predictions = input.Predictions with { Model2026TrainedThroughUtc = null } },
        new BotGConfiguration()));
}

void ProbabilityMonotonicityGate()
{
    var method = typeof(BotGAutomationService).GetMethod(
        "ApplyProbabilityMonotonicityGate",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G probability monotonicity gate was not found.");
    BotGCandidate Candidate(BotGSelection side, decimal line, double probability, string bookmaker) => new()
    {
        FixtureId = 42,
        Bookmaker = bookmaker,
        MarketType = BotGMarketType.TotalGoals,
        Selection = side,
        Line = line,
        FinalProbability = probability,
        Decision = BotGDecisionStatus.Approved,
        DecisionReason = BotGDecisionReason.Approved,
        DecisionReasons = [BotGDecisionReason.Approved]
    };
    var input = new[]
    {
        Candidate(BotGSelection.Over, 2.5m, 0.50d, "Book"),
        Candidate(BotGSelection.Over, 3.5m, 0.60d, "book"),
        Candidate(BotGSelection.Under, 2.5m, 0.40d, "Book"),
        Candidate(BotGSelection.Under, 3.5m, 0.55d, "Book"),
        Candidate(BotGSelection.Under, 2.5m, 0.30d, "Other"),
        Candidate(BotGSelection.Under, 3.5m, 0.70d, "Other")
    };
    var gated = (IReadOnlyList<BotGCandidate>)method.Invoke(null, [input])!;
    Check.True(gated.Take(2).All(candidate =>
        candidate.Decision == BotGDecisionStatus.Abstain
        && candidate.DecisionReasons.Contains(BotGDecisionReason.PredictionMonotonicityViolation)));
    Check.True(gated.Skip(2).All(candidate => candidate.Decision == BotGDecisionStatus.Approved));

    var underViolation = new[]
    {
        Candidate(BotGSelection.Under, 2.5m, 0.60d, "Book"),
        Candidate(BotGSelection.Under, 3.5m, 0.50d, "Book")
    };
    var underGated = (IReadOnlyList<BotGCandidate>)method.Invoke(null, [underViolation])!;
    Check.True(underGated.All(candidate =>
        candidate.Decision == BotGDecisionStatus.Abstain
        && candidate.DecisionReason == BotGDecisionReason.PredictionMonotonicityViolation));
}

void AutomationHistoryMapping()
{
    var rows = new[]
    {
        ApiHistory(1, new DateOnly(2026, 5, 30), "Target", "First", 2, 1),
        ApiHistory(2, new DateOnly(2026, 5, 29), "Second", "Target", 1, 3),
        ApiHistory(3, new DateOnly(2026, 6, 1), "Target", "SameDay", 9, 0),
        ApiHistory(4, new DateOnly(2026, 5, 28), "Foreign", "Other", 0, 8),
        ApiHistory(5, new DateOnly(2026, 6, 2), "Target", "Future", 7, 0)
    };
    var method = typeof(BotGAutomationService).GetMethod(
        "MapHistory",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G MapHistory helper was not found.");
    var mapped = (IReadOnlyList<BotGHistoryObservation>)(method.Invoke(
        null,
        [rows, "Target", AsOfUtc])
        ?? throw new InvalidOperationException("Bot G MapHistory returned null."));
    Check.Equal(2, mapped.Count);
    Check.Close(2d, mapped.Single(row => row.FixtureId == 1).ValueFor);
    Check.Close(1d, mapped.Single(row => row.FixtureId == 1).ValueAgainst);
    Check.Close(3d, mapped.Single(row => row.FixtureId == 2).ValueFor);
    Check.Close(1d, mapped.Single(row => row.FixtureId == 2).ValueAgainst);
    Check.True(mapped.All(row => row.MatchDateUtc.Kind == DateTimeKind.Utc));

    var beforeSantiagoMidnight = new DateTime(2026, 6, 2, 2, 0, 0, DateTimeKind.Utc);
    var localSameDay = (IReadOnlyList<BotGHistoryObservation>)method.Invoke(
        null,
        [new[] { ApiHistory(6, new DateOnly(2026, 6, 1), "Target", "LocalSameDay", 1, 0) }, "Target", beforeSantiagoMidnight])!;
    Check.Equal(0, localSameDay.Count, "A Santiago same-day result must not enter an intraday prediction.");

    var filterContext = typeof(BotGAutomationService).GetMethod(
        "FilterContextAsOf",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G FilterContextAsOf helper was not found.");
    var context = new PredictionContextDto(
        new PredictionComparisonDto(0d, null, "test"),
        rows,
        rows,
        rows,
        rows);
    var filtered = (PredictionContextDto)filterContext.Invoke(null, [context, AsOfUtc, "Target", "Other"])!;
    Check.True(filtered.HomeGeneralMatches.All(row => row.MatchDate < new DateOnly(2026, 6, 1)));
    Check.Equal(2, filtered.HomeGeneralMatches.Count);
    Check.True(filtered.HomeGeneralMatches.All(row =>
        row.HomeTeam == "Target" || row.AwayTeam == "Target"));
}

void AutomationTemporalHelpers()
{
    var ensureUtc = typeof(BotGAutomationService).GetMethod(
        "EnsureUtc",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G EnsureUtc helper was not found.");
    var unspecified = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Unspecified);
    var normalizedUnspecified = (DateTime)ensureUtc.Invoke(null, [unspecified])!;
    Check.Equal(DateTimeKind.Utc, normalizedUnspecified.Kind);
    Check.Equal(unspecified.Ticks, normalizedUnspecified.Ticks);
    var local = new DateTime(2026, 5, 31, 10, 0, 0, DateTimeKind.Local);
    var normalizedLocal = (DateTime)ensureUtc.Invoke(null, [local])!;
    Check.Equal(local.ToUniversalTime(), normalizedLocal);

    var snapshotOdds = typeof(BotGAutomationService).GetMethod(
        "WithSnapshotOdds",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G WithSnapshotOdds helper was not found.");
    var liveOnly = new UpcomingOddsRecord { OverOdds = 1.91m, UnderOdds = 1.93m };
    var immutableOnly = (UpcomingOddsRecord)snapshotOdds.Invoke(null, [liveOnly])!;
    Check.True(immutableOnly.OverOdds is null);
    Check.True(immutableOnly.UnderOdds is null);

    var resolveFixture = typeof(BotGAutomationService).GetMethod(
        "ResolveFixtureGroupId",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G ResolveFixtureGroupId helper was not found.");
    var canonical = new UpcomingOddsRecord
    {
        ApiFootballFixtureId = 12345,
        MatchDate = new DateTime(2026, 6, 10, 20, 0, 0),
        League = "League",
        HomeTeam = "Home",
        AwayTeam = "Away"
    };
    var withoutOfficialId = canonical with { ApiFootballFixtureId = null, Source = "Pinnacle" };
    var groupId = (long)resolveFixture.Invoke(
        null,
        [new[] { canonical, withoutOfficialId }])!;
    var fallbackGroupId = (long)resolveFixture.Invoke(null, [new[] { withoutOfficialId }])!;
    Check.Equal(fallbackGroupId, groupId);
    Check.True(groupId != 12345L,
        "Canonical fixture identity must remain provider-independent after official-id enrichment.");
    var conflictRejected = false;
    try
    {
        resolveFixture.Invoke(null, [new[] { canonical, canonical with { ApiFootballFixtureId = 54321 } }]);
    }
    catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException)
    {
        conflictRejected = true;
    }
    Check.True(conflictRejected, "Conflicting official fixture IDs must be rejected.");

    var selectBatch = typeof(AutomatedCornersSelectionService).GetMethod(
        "SelectBatchOddsRows",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G fixture-complete batching helper was not found.");
    var secondLine = canonical with { MarketType = "GoalsTotal", LineValue = 3.5m };
    var firstLine = canonical with { MarketType = "GoalsTotal", LineValue = 2.5m };
    var otherFixture = canonical with
    {
        MatchDate = canonical.MatchDate.AddHours(1),
        HomeTeam = "Other Home",
        AwayTeam = "Other Away",
        MarketType = "GoalsTotal",
        LineValue = 2.5m
    };
    var completeBatch = (UpcomingOddsRecord[])selectBatch.Invoke(
        null,
        [new[] { firstLine, secondLine, otherFixture }, 0, 1, true])!;
    Check.Equal(2, completeBatch.Length);
    Check.True(completeBatch.All(row => row.HomeTeam == "Home"),
        "A G batch must contain the entire first fixture even when BatchSize is one.");

    var findSource = typeof(AutomatedCornersSelectionService).GetMethod(
        "FindBotGSourceOdds",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Bot G source-quote helper was not found.");
    var fallbackSource = canonical with
    {
        ApiFootballFixtureId = null,
        Source = "Betano",
        MarketType = "GoalsTotal",
        LineValue = 2.5m,
        OddsSnapshotId = 77,
        SnapshotOverOdds = 1.91m,
        SnapshotUnderOdds = 1.93m
    };
    var auditedFallbackId = (long)resolveFixture.Invoke(null, [new[] { fallbackSource }])!;
    var sourceCandidate = new BotGCandidate
    {
        FixtureId = auditedFallbackId,
        SourceOddsId = 77,
        Bookmaker = "Betano",
        MarketType = BotGMarketType.TotalGoals,
        Selection = BotGSelection.Over,
        Line = 2.5m,
        OverOdds = 1.91m,
        UnderOdds = 1.93m
    };
    var resolvedSource = (UpcomingOddsRecord)findSource.Invoke(
        null,
        [new[] { fallbackSource }, sourceCandidate])!;
    Check.True(resolvedSource.ApiFootballFixtureId is null,
        "A canonical audit hash must never be persisted as an official API-Football fixture ID.");
}

void AutomationPublicationGuard()
{
    var service = new BotGAutomationService(
        null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!);
    Check.Throws<InvalidOperationException>(() => service.MarkPublishedAsync(
        new BotGCandidate { Decision = BotGDecisionStatus.Abstain },
        1,
        CancellationToken.None).GetAwaiter().GetResult());
}

void MetaNeutrality()
{
    const double market = 0.537d;
    Check.Close(market, BotGLogitResidual.Apply(market, 0d), 1e-12d);
    var service = new InMemoryBotGMetaModelService(Artifact(new BotGLogitResidualLogisticModel()));
    var prediction = service.Predict(MetaInput(market, new Dictionary<string, double>()));
    Check.True(prediction.IsAvailable);
    Check.Close(market, prediction.Probability, 1e-12d);
    Check.Close(0d, prediction.ResidualLogit);
}

void MetaUnavailable()
{
    var model = new BotGLogitResidualLogisticModel
    {
        Features = [new BotGMetaFeatureCoefficient("edgeFeature", 0d, 1d, 1d)]
    };
    var service = new InMemoryBotGMetaModelService(Artifact(model));
    var missing = service.Predict(MetaInput(0.5d, new Dictionary<string, double>()));
    Check.False(missing.IsAvailable);
    Check.Contains("missing", missing.UnavailableReason);
    var schema = service.Predict(MetaInput(0.5d, new Dictionary<string, double> { ["edgeFeature"] = 0d }) with
    {
        FeatureSchemaVersion = "wrong-schema"
    });
    Check.False(schema.IsAvailable);
    Check.Contains("schema", schema.UnavailableReason);
    Check.False(new UnavailableBotGMetaModelService().Predict(MetaInput(0.5d, new Dictionary<string, double>())).IsAvailable);
}

void MetaDeploymentGate()
{
    var valid = Artifact(new BotGLogitResidualLogisticModel());
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with { Deployable = false }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with { Synthetic = true }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with
    {
        ConfigurationVersion = "wrong-configuration"
    }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with { Family = "SHOTS" }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with
    {
        SupportedMarkets = [BotGMarketType.TotalGoals]
    }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with
    {
        Training = valid.Training with { LegacyModelVersions = [] }
    }));
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(valid with
    {
        Uncertainty = valid.Uncertainty with { Method = "unknown-uncertainty-method" }
    }));
}

void MetaCompatibilityGate()
{
    var artifact = Artifact(new BotGLogitResidualLogisticModel());
    var service = new InMemoryBotGMetaModelService(artifact);
    var input = MetaInput(0.5d, new Dictionary<string, double>());

    var configurationMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            ConfigurationVersion = "bot-g-goals-market-2.0.0"
        }
    });
    Check.False(configurationMismatch.IsAvailable);
    Check.Contains("Configuration version", configurationMismatch.UnavailableReason);

    var metaMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            MetaModel = input.RuntimeConfiguration.MetaModel with { ModelVersion = "wrong-meta" }
        }
    });
    Check.False(metaMismatch.IsAvailable);
    Check.Contains("Meta-model version", metaMismatch.UnavailableReason);

    var marketContractMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            SupportedMarkets = [BotGMarketType.TotalGoals]
        }
    });
    Check.False(marketContractMismatch.IsAvailable);
    Check.Contains("supported GOALS markets", marketContractMismatch.UnavailableReason);

    var runtimeSettingsMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            MetaModel = input.RuntimeConfiguration.MetaModel with
            {
                MaximumAbsoluteResidualLogit = 5d
            }
        }
    });
    Check.False(runtimeSettingsMismatch.IsAvailable);
    Check.Contains("meta/settlement settings", runtimeSettingsMismatch.UnavailableReason);

    var legacyMismatch = service.Predict(input with { LegacyModelVersion = "wrong-legacy" });
    Check.False(legacyMismatch.IsAvailable);
    Check.Contains("Legacy base-model version", legacyMismatch.UnavailableReason);

    var baseMismatch = service.Predict(input with { Model2026Version = "wrong-2026" });
    Check.False(baseMismatch.IsAvailable);
    Check.Contains("Models 2026 version", baseMismatch.UnavailableReason);

    var lineageArtifact = artifact with
    {
        Training = artifact.Training with { Model2026Versions = ["other-trained-version"] }
    };
    var lineageMismatch = new InMemoryBotGMetaModelService(lineageArtifact).Predict(input);
    Check.False(lineageMismatch.IsAvailable);
    Check.Contains("training lineage", lineageMismatch.UnavailableReason);

    var uncertaintyMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            Uncertainty = input.RuntimeConfiguration.Uncertainty with { ConfidenceZScore = 2d }
        }
    });
    Check.False(uncertaintyMismatch.IsAvailable);
    Check.Contains("uncertainty", uncertaintyMismatch.UnavailableReason);

    var oodMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            OutOfDistribution = input.RuntimeConfiguration.OutOfDistribution with
            {
                MinimumReferenceSampleSize = 31
            }
        }
    });
    Check.False(oodMismatch.IsAvailable);
    Check.Contains("OOD", oodMismatch.UnavailableReason);

    var calibrationMismatch = service.Predict(input with
    {
        RuntimeConfiguration = input.RuntimeConfiguration with
        {
            Calibration = input.RuntimeConfiguration.Calibration with
            {
                Version = "wrong-calibration"
            }
        }
    });
    Check.False(calibrationMismatch.IsAvailable);
    Check.Contains("calibration identity", calibrationMismatch.UnavailableReason);
}

void MetaMalformedEvidence()
{
    var valid = Artifact(new BotGLogitResidualLogisticModel());
    var malformedCalibration = valid with
    {
        Calibration =
        [
            new BotGCalibrationProfile
            {
                Key = null!,
                Version = "invalid",
                EffectiveSampleSize = 10d,
                EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-2)
            }
        ]
    };
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(malformedCalibration));

    var malformedOod = valid with
    {
        OodFeatureStats = [new BotGOodFeatureReference("", 0d, 1d, -1d, 1d, 100)]
    };
    Check.Throws<ArgumentException>(() => new InMemoryBotGMetaModelService(malformedOod));
}

void MetaTemporalAndEnsemble()
{
    var artifact = Artifact(new BotGLogitResidualLogisticModel()) with
    {
        Ensemble =
        [
            new BotGEnsembleMember { Name = "positive", Intercept = 0.5d },
            new BotGEnsembleMember { Name = "negative", Intercept = -0.5d }
        ],
        SettlementProfiles =
        [
            new BotGSettlementDistributionProfile
            {
                Distribution = BotGOutcomeDistribution.Binary(0.2d),
                EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-3),
                EffectiveSampleSize = 100d
            },
            new BotGSettlementDistributionProfile
            {
                Line = 2.25m,
                Distribution = BotGOutcomeDistribution.Binary(0.7d),
                EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-3),
                EffectiveSampleSize = 80d
            },
            new BotGSettlementDistributionProfile
            {
                Line = 2.75m,
                Distribution = BotGOutcomeDistribution.Binary(0.9d),
                EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-3),
                EffectiveSampleSize = 200d
            }
        ]
    };
    var service = new InMemoryBotGMetaModelService(artifact);
    var prediction = service.Predict(MetaInput(0.5d, new Dictionary<string, double>()) with { Line = 2.25m });
    Check.True(prediction.IsAvailable);
    Check.True(prediction.EnsembleDispersion > 0d);
    Check.Equal("model-test", prediction.ModelVersion);
    Check.Close(0.7d, prediction.SettlementDistribution!.Win);
    var wildcard = service.Predict(MetaInput(0.5d, new Dictionary<string, double>()) with { Line = 3.25m });
    Check.Close(0.2d, wildcard.SettlementDistribution!.Win);
    var leakage = service.Predict(MetaInput(0.5d, new Dictionary<string, double>()) with
    {
        PredictionTimestampUtc = artifact.TrainedThroughUtc
    });
    Check.False(leakage.IsAvailable);
    Check.Contains("trained-through", leakage.UnavailableReason);
}

void CalibrationHierarchy()
{
    var profiles = CalibrationProfiles();
    var result = new BotGHierarchicalCalibrationService().Calibrate(
        CalibrationInput(BotGMarketType.TotalGoals, BotGSelection.Over, "Betano", profiles),
        new BotGConfiguration());
    Check.True(result.IsAvailable);
    Check.Equal(BotGCalibrationLevel.MarketTypeSelectionAndBookmaker, result.MostSpecificLevel);
    Check.SequenceEqual(
        new[]
        {
            BotGCalibrationLevel.GlobalGoals,
            BotGCalibrationLevel.MarketType,
            BotGCalibrationLevel.MarketTypeAndSelection,
            BotGCalibrationLevel.MarketTypeSelectionAndBookmaker
        },
        result.AppliedLevels);
    Check.True(result.Reliability > 0.3d);
    Check.Close(100d, result.EffectiveSampleSize);
}

void CalibrationIsolation()
{
    var service = new BotGHierarchicalCalibrationService();
    var profiles = CalibrationProfiles();
    var otherBookmaker = service.Calibrate(
        CalibrationInput(BotGMarketType.TotalGoals, BotGSelection.Over, "Pinnacle", profiles),
        new BotGConfiguration());
    Check.Equal(BotGCalibrationLevel.MarketTypeAndSelection, otherBookmaker.MostSpecificLevel);

    var otherSide = service.Calibrate(
        CalibrationInput(BotGMarketType.TotalGoals, BotGSelection.Under, "Betano", profiles),
        new BotGConfiguration());
    Check.Equal(BotGCalibrationLevel.MarketType, otherSide.MostSpecificLevel);

    var otherMarket = service.Calibrate(
        CalibrationInput(BotGMarketType.HomeTeamGoals, BotGSelection.Over, "Betano", profiles),
        new BotGConfiguration());
    Check.Equal(BotGCalibrationLevel.GlobalGoals, otherMarket.MostSpecificLevel);

    var shotsOnly = new[] { Profile("SHOTS", null, null, null, "shots", 2d) };
    Check.False(service.Calibrate(
        CalibrationInput(BotGMarketType.TotalGoals, BotGSelection.Over, "Betano", shotsOnly),
        new BotGConfiguration()).IsAvailable);
}

void CalibrationTemporalLag()
{
    var futureEvidence = Profile("GOALS", null, null, null, "too-new", 0d) with
    {
        EvidenceAvailableThroughUtc = AsOfUtc.AddHours(-4)
    };
    var result = new BotGHierarchicalCalibrationService().Calibrate(
        CalibrationInput(BotGMarketType.TotalGoals, BotGSelection.Over, "Betano", [futureEvidence]),
        new BotGConfiguration());
    Check.False(result.IsAvailable);
    Check.Contains("leakage-safe", result.UnavailableReason);
}

void ConservativeProbabilityAndEdge()
{
    var service = new BotGConservativeUncertaintyService();
    var result = service.Estimate(new BotGUncertaintyInput(0.60d, 0.01d, 100d), new BotGConfiguration());
    Check.True(result.ProbabilityLowerBound <= result.FinalProbability);
    Check.True(result.ConservativeProbability <= result.FinalProbability);
    Check.True(result.ProbabilityUpperBound >= result.FinalProbability);
    var nominal = BotGConservativeMetrics.Edge(result.FinalProbability, 0.52d);
    var conservative = BotGConservativeMetrics.ConservativeEdge(result, 0.52d);
    Check.True(conservative <= nominal);
}

void ExpectedValueDistribution()
{
    var distribution = new BotGOutcomeDistribution(0.4d, 0.1d, 0.2d, 0.1d, 0.2d);
    var service = new BotGExpectedValueService();
    var result = service.Calculate(2m, distribution);
    Check.Close(0.2d, result.ExpectedProfitPerUnit);
    Check.Close(0.5d, result.PositiveReturnProbability);
    Check.Close(0.3d, result.NegativeReturnProbability);
    var reanchored = service.Reanchor(distribution, 0.60d);
    Check.Close(0.60d, reanchored.PositiveReturnProbability);
    Check.Close(4d, reanchored.Win / reanchored.HalfWin);
    Check.Close(1d, reanchored.Total);
    Check.Throws<ArgumentException>(() => service.Calculate(
        2m,
        new BotGOutcomeDistribution(1d, 1d, 0d, 0d, 0d)));
}

void ConservativeExpectedValue()
{
    var service = new BotGExpectedValueService();
    var distribution = new BotGOutcomeDistribution(0.4d, 0.1d, 0.2d, 0.1d, 0.2d);
    var nominal = service.Calculate(2m, distribution);
    var conservative = service.CalculateConservative(2m, distribution, 0.05d, new BotGConfiguration());
    Check.True(conservative.ExpectedProfitPerUnit < nominal.ExpectedProfitPerUnit);
    Check.Close(1d, conservative.Distribution.Total);
}

void RobustOod()
{
    var references = new[] { new BotGOodFeatureReference("x", 0d, 1d, -3d, 3d, 100) };
    var service = new BotGRobustOodService();
    var normal = service.Evaluate(
        new BotGOodInput(new Dictionary<string, double> { ["x"] = 0.1d }, references),
        new BotGConfiguration());
    Check.True(normal.IsAvailable);
    Check.Close(0d, normal.Score);
    var severe = service.Evaluate(
        new BotGOodInput(new Dictionary<string, double> { ["x"] = 20d }, references),
        new BotGConfiguration());
    Check.Close(1d, severe.Score);
    Check.SequenceEqual(new[] { "x" }, severe.OutlyingFeatures);
}

void OodUnavailable()
{
    var service = new BotGRobustOodService();
    var reference = new BotGOodFeatureReference("required", 0d, 1d, -3d, 3d, 100);
    var missing = service.Evaluate(
        new BotGOodInput(new Dictionary<string, double>(), [reference]),
        new BotGConfiguration());
    Check.False(missing.IsAvailable);
    Check.Close(1d, missing.Score);
    var insufficient = service.Evaluate(
        new BotGOodInput(new Dictionary<string, double> { ["required"] = 0d }, [reference with { SampleSize = 2 }]),
        new BotGConfiguration());
    Check.False(insufficient.IsAvailable);
}

void AbstentionApproved()
{
    var service = new BotGAbstentionService();
    var decision = service.Decide(SafeDecisionInput(), new BotGConfiguration());
    Check.Equal(BotGDecisionStatus.Approved, decision.Status);
    Check.Equal(BotGDecisionReason.Approved, decision.PrimaryReason);
    var halfLine = SafeDecisionInput();
    halfLine = halfLine with
    {
        Quote = halfLine.Quote with { Line = 2.5m },
        SettlementDistributionAvailable = false
    };
    Check.Equal(BotGDecisionStatus.Approved, service.Decide(halfLine, new BotGConfiguration()).Status);
}

void AbstentionAndRejection()
{
    var service = new BotGAbstentionService();
    var missingSettlementDistribution = service.Decide(
        SafeDecisionInput() with { SettlementDistributionAvailable = false },
        new BotGConfiguration());
    Check.Equal(BotGDecisionStatus.Abstain, missingSettlementDistribution.Status);
    Check.True(missingSettlementDistribution.Reasons.Contains(BotGDecisionReason.SettlementDistributionUnavailable));

    var unsafeModel = service.Decide(
        SafeDecisionInput() with { MetaPrediction = BotGMetaModelPrediction.Unavailable("artifact absent") },
        new BotGConfiguration());
    Check.Equal(BotGDecisionStatus.Abstain, unsafeModel.Status);
    Check.True(unsafeModel.Reasons.Contains(BotGDecisionReason.ModelUnavailable));

    var ood = service.Decide(
        SafeDecisionInput() with
        {
            OutOfDistribution = new BotGOodResult(true, 0.9d, new Dictionary<string, double>(), ["x"], "ood")
        },
        new BotGConfiguration());
    Check.Equal(BotGDecisionStatus.Abstain, ood.Status);
    Check.True(ood.Reasons.Contains(BotGDecisionReason.OutOfDistribution));

    var lowValue = service.Decide(
        SafeDecisionInput() with { ConservativeExpectedValue = -0.01d },
        new BotGConfiguration());
    Check.Equal(BotGDecisionStatus.Rejected, lowValue.Status);
    Check.True(lowValue.Reasons.Contains(BotGDecisionReason.LowConservativeExpectedValue));
}

void RankingOnePerFixture()
{
    var selector = new BotGSelector();
    var low = Candidate(10, 0.03d, 0.025d, Guid.Parse("00000000-0000-0000-0000-000000000001"));
    var high = Candidate(10, 0.12d, 0.08d, Guid.Parse("00000000-0000-0000-0000-000000000002"));
    var other = Candidate(11, 0.05d, 0.04d, Guid.Parse("00000000-0000-0000-0000-000000000003"));
    var rejected = Candidate(12, 1d, 1d, Guid.Parse("00000000-0000-0000-0000-000000000004")) with
    {
        Decision = BotGDecisionStatus.Rejected
    };
    var selected = selector.SelectBestPerFixture([low, high, other, rejected], new BotGConfiguration());
    Check.Equal(2, selected.Count);
    Check.Equal(1, selected.Count(candidate => candidate.FixtureId == 10));
    Check.Equal(high.CandidateUuid, selected.Single(candidate => candidate.FixtureId == 10).CandidateUuid);
    Check.False(selected.Any(candidate => candidate.FixtureId == 12));
}

void AsianSettlementMatrix()
{
    var lines = new[] { 1.25m, 1.75m, 2.25m, 2.75m, 3.25m, 3.75m, 4.25m, 4.75m };
    var markets = new[]
    {
        BotGMarketType.TotalGoals,
        BotGMarketType.HomeTeamGoals,
        BotGMarketType.AwayTeamGoals
    };
    Check.False(BotGAsianSettlementCalculator.RequiresFiveStateDistribution(2.5m));
    Check.True(BotGAsianSettlementCalculator.RequiresFiveStateDistribution(2m));
    Check.True(BotGAsianSettlementCalculator.RequiresFiveStateDistribution(2.25m));
    Check.True(BotGAsianSettlementCalculator.RequiresFiveStateDistribution(2.75m));
    foreach (var market in markets)
    foreach (var line in lines)
    {
        var floor = decimal.ToInt32(decimal.Floor(line));
        var isQuarter = line - decimal.Floor(line) == 0.25m;
        var actual = isQuarter ? floor : floor + 1;
        var goals = GoalsFor(market, actual);
        var over = BotGAsianSettlementCalculator.Calculate(market, BotGSelection.Over, line, goals.Home, goals.Away, 2m);
        var under = BotGAsianSettlementCalculator.Calculate(market, BotGSelection.Under, line, goals.Home, goals.Away, 2m);
        Check.Equal(isQuarter ? BotGSettlementState.HalfLoss : BotGSettlementState.HalfWin, over.State,
            $"Over {market} {line} actual {actual}");
        Check.Equal(isQuarter ? BotGSettlementState.HalfWin : BotGSettlementState.HalfLoss, under.State,
            $"Under {market} {line} actual {actual}");
        Check.Close(isQuarter ? -0.5m : 0.5m, over.Factor);
        Check.Close(isQuarter ? 0.5m : -0.5m, under.Factor);

        var high = GoalsFor(market, decimal.ToInt32(decimal.Ceiling(line)) + 1);
        var low = GoalsFor(market, Math.Max(0, floor - 1));
        Check.Equal(BotGSettlementState.Win,
            BotGAsianSettlementCalculator.Calculate(market, BotGSelection.Over, line, high.Home, high.Away, 2m).State);
        Check.Equal(BotGSettlementState.Win,
            BotGAsianSettlementCalculator.Calculate(market, BotGSelection.Under, line, low.Home, low.Away, 2m).State);
    }
}

BotGMarketQuote Quote(
    BotGMarketType market = BotGMarketType.TotalGoals,
    BotGSelection selection = BotGSelection.Over,
    decimal line = 2.25m,
    decimal? overOdds = 1.90m,
    decimal? underOdds = 1.95m) => new(
        100,
        AsOfUtc.AddDays(1),
        AsOfUtc,
        AsOfUtc.AddMinutes(-10),
        "Test League",
        "2026",
        "Home",
        "Away",
        "Betano",
        market,
        selection,
        line,
        overOdds,
        underOdds);

BotGFeatures BuildFeatures(BotGMarketQuote? quote = null)
{
    var value = quote ?? Quote();
    return new BotGFeatureBuilder().Build(FeatureInput(value), new BotGConfiguration());
}

BotGFeatureBuildInput FeatureInput(BotGMarketQuote quote)
{
    var probability = new StrictMarketProbabilityService().Calculate(quote);
    var history = Enumerable.Range(0, 25)
        .Select(index => new BotGHistoryObservation(
            1_000 + index,
            AsOfUtc.AddDays(-index - 1),
            index + 1,
            0d))
        .ToArray();
    var predictions = new BotGBasePredictions
    {
        LegacyTotal = 2.6d,
        LegacyHome = 1.5d,
        LegacyAway = 1.1d,
        Model2026Total = 2.7d,
        Model2026Home = 1.55d,
        Model2026Away = 1.15d,
        LegacyTrainedThroughUtc = AsOfUtc.AddDays(-30),
        Model2026TrainedThroughUtc = AsOfUtc.AddDays(-20)
    };
    return new BotGFeatureBuildInput(
        quote,
        predictions,
        history,
        history,
        history,
        history,
        probability,
        0.55d,
        0.50d,
        0.60d,
        0.08d,
        100);
}

BotGModelArtifact Artifact(BotGLogitResidualLogisticModel model) => new()
{
    ConfigurationVersion = BotGConfiguration.DefaultConfigurationVersion,
    Family = "GOALS",
    SupportedMarkets =
    [
        BotGMarketType.TotalGoals,
        BotGMarketType.HomeTeamGoals,
        BotGMarketType.AwayTeamGoals
    ],
    ModelVersion = "model-test",
    FeatureSchemaVersion = BotGConfiguration.DefaultFeatureSchemaVersion,
    TrainedThroughUtc = AsOfUtc.AddDays(-30),
    Deployable = true,
    Synthetic = false,
    RuntimeSettings = new BotGArtifactRuntimeSettings
    {
        MaximumAbsoluteResidualLogit = 4d,
        MinimumSettlementEffectiveSampleSize = 40d,
        SettlementEvidenceLagHours = 8
    },
    Uncertainty = new BotGArtifactUncertaintySettings
    {
        Version = "bot-g-uncertainty-1.0.0",
        Method = "fixture-cluster bootstrap dispersion plus calibration sampling error",
        ConfidenceZScore = 1.645d,
        ConservativeLambda = 1d,
        UseLowerBound = true,
        MinimumUncertainty = 0.005d,
        MaximumUncertainty = 0.25d
    },
    Ood = new BotGArtifactOodSettings
    {
        Version = "bot-g-ood-1.0.0",
        Method = "robust-mad-percentile-v1",
        MinimumReferenceSampleSize = 30,
        RobustZScoreThreshold = 3.5d,
        SevereRobustZScore = 8d
    },
    Training = new BotGArtifactTrainingMetadata
    {
        LegacyModelVersions = ["legacy-test"],
        Model2026Versions = ["2026-test"]
    },
    Model = model,
    Calibration =
    [
        new BotGCalibrationProfile
        {
            Key = BotGCalibrationKey.GlobalGoals,
            Version = "bot-g-calibration-1.0.0",
            EffectiveSampleSize = 100d,
            SampleSize = 100,
            EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-3)
        }
    ]
};

BotGMetaModelInput MetaInput(double market, IReadOnlyDictionary<string, double> vector) => new(
    BotGConfiguration.DefaultFeatureSchemaVersion,
    AsOfUtc,
    BotGMarketType.TotalGoals,
    BotGSelection.Over,
    "Betano",
    market,
    vector,
    new BotGConfiguration
    {
        LegacyModelVersion = "legacy-test",
        Model2026Version = "2026-test",
        MetaModel = new BotGMetaModelConfiguration { ModelVersion = "model-test" }
    },
    "legacy-test",
    "2026-test",
    "Test League");

IReadOnlyList<BotGCalibrationProfile> CalibrationProfiles() =>
[
    Profile("GOALS", null, null, null, "global", 0.05d),
    Profile("GOALS", BotGMarketType.TotalGoals, null, null, "total", 0.10d),
    Profile("GOALS", BotGMarketType.TotalGoals, BotGSelection.Over, null, "total-over", 0.15d),
    Profile("GOALS", BotGMarketType.TotalGoals, BotGSelection.Over, "Betano", "total-over-betano", 0.20d),
    Profile("GOALS", BotGMarketType.HomeTeamGoals, BotGSelection.Under, "Betano", "wrong-market-side", 5d),
    Profile("SHOTS", BotGMarketType.TotalGoals, BotGSelection.Over, "Betano", "wrong-family", 5d)
];

BotGCalibrationProfile Profile(
    string family,
    BotGMarketType? market,
    BotGSelection? side,
    string? bookmaker,
    string version,
    double intercept) => new()
{
    Key = new BotGCalibrationKey(family, market, side, bookmaker),
    Version = version,
    Intercept = intercept,
    SampleSize = 120,
    EffectiveSampleSize = 100d,
    EvidenceAvailableThroughUtc = AsOfUtc.AddDays(-3)
};

BotGCalibrationInput CalibrationInput(
    BotGMarketType market,
    BotGSelection side,
    string bookmaker,
    IReadOnlyList<BotGCalibrationProfile> profiles) => new(
        AsOfUtc,
        market,
        side,
        bookmaker,
        0.55d,
        profiles);

BotGDecisionInput SafeDecisionInput()
{
    var quote = Quote(overOdds: 1.90m, underOdds: 1.90m);
    return new BotGDecisionInput
    {
        Quote = quote,
        MarketProbability = new StrictMarketProbabilityService().Calculate(quote),
        MetaPrediction = new BotGMetaModelPrediction(
            true, 0.61d, 0.3d, "meta", BotGConfiguration.DefaultFeatureSchemaVersion,
            AsOfUtc.AddDays(-30), 0.01d),
        Calibration = new BotGCalibrationResult(
            true, 0.61d, 0.60d, 0.80d, 100d, "cal", BotGCalibrationLevel.GlobalGoals,
            [BotGCalibrationLevel.GlobalGoals]),
        Uncertainty = new BotGUncertaintyResult(0.60d, 0.56d, 0.64d, 0.03d, 0.56d, "unc"),
        OutOfDistribution = new BotGOodResult(true, 0.1d, new Dictionary<string, double>(), [], "ood"),
        SettlementDistributionAvailable = true,
        FinalProbability = 0.60d,
        ConservativeEdge = 0.06d,
        ConservativeExpectedValue = 0.07d,
        DataQualityScore = 0.90d,
        ContextAgreementScore = 0.90d,
        ModelDisagreement = 0.20d,
        HistoricalMatches = 20
    };
}

BotGCandidate Candidate(long fixtureId, double ev, double edge, Guid uuid) => new()
{
    CandidateUuid = uuid,
    FixtureId = fixtureId,
    FixtureDateUtc = AsOfUtc.AddDays(1),
    Decision = BotGDecisionStatus.Approved,
    ConservativeExpectedValue = ev,
    ConservativeEdge = edge,
    CalibrationReliability = 0.8d,
    DataQualityScore = 0.9d,
    ProbabilityUncertainty = 0.03d,
    ContextAgreementScore = 0.9d
};

(int Home, int Away) GoalsFor(BotGMarketType market, int actual) => market switch
{
    BotGMarketType.TotalGoals => (actual, 0),
    BotGMarketType.HomeTeamGoals => (actual, 7),
    BotGMarketType.AwayTeamGoals => (7, actual),
    _ => throw new ArgumentOutOfRangeException(nameof(market))
};

MatchHistoryItemDto ApiHistory(
    int id,
    DateOnly date,
    string home,
    string away,
    int homeGoals,
    int awayGoals) => new(
        id,
        "Test League",
        "2026",
        date,
        false,
        home,
        away,
        null,
        null,
        0,
        0,
        homeGoals,
        awayGoals,
        0,
        0,
        0,
        0,
        50d,
        50d,
        0);

static class Check
{
    public static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Expected true.");
    }

    public static void False(bool value, string? message = null) => True(!value, message ?? "Expected false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
    }

    public static void Close(double expected, double actual, double tolerance = 1e-9d)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected:R}, got {actual:R} (tolerance {tolerance:R}).");
    }

    public static void Close(decimal expected, decimal actual, decimal tolerance = 0.000001m)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected}, got {actual} (tolerance {tolerance}).");
    }

    public static void Contains(string expectedPart, string? actual)
    {
        if (actual?.Contains(expectedPart, StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expectedPart}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
