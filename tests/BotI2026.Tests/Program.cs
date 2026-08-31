using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CornersPrediction.Application.Automation.BotI;
using Microsoft.SqlServer.TransactSql.ScriptDom;

var tests = new (string Name, Action Run)[]
{
    ("No-vig probability removes bilateral margin", NoVigProbability),
    ("Canonical fixture identity is explicit big-endian", CanonicalFixtureIdentityIsPortable),
    ("Strong opening-to-current move approves Over in shadow", StrongMoveApprovesOver),
    ("Strong reverse move approves Under in shadow", StrongMoveApprovesUnder),
    ("Weak movement is rejected instead of invented", WeakMovementIsRejected),
    ("Insufficient or stale evidence abstains", MissingEvidenceAbstains),
    ("Future decision evidence fails closed", FutureEvidenceAbstains),
    ("Sharp cross-book contradiction rejects", CrossBookContradictionRejects),
    ("Feature snapshot declares no outcomes and publication block", SnapshotIsHonest),
    ("Idempotency key is stable and snapshot-specific", IdempotencyIsStable),
    ("Collector is append-idempotent and never publishes", CollectorIsIdempotent),
    ("Migration is append-only, outcome-aware and hard-blocked", MigrationGuardsShadowOnly),
    ("Migration parses with SQL Server ScriptDom", MigrationParses),
    ("Timeline collector SQL parses with SQL Server ScriptDom", TimelineSqlParses),
    ("API exposes collection/read surface but no publication", ApiSurfaceIsSafe),
    ("Automatic collector is enabled and isolated", AutomaticCollectorIsConfigured)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"Bot I2026 tests: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void NoVigProbability()
{
    Near(0.5m, BotIMarketMovementEvaluator.NoVigOver(1.91m, 1.91m), 0.0000001m);
    Near(0.6m, BotIMarketMovementEvaluator.NoVigOver(1.50m, 2.25m), 0.0000001m);
    Throws<ArgumentOutOfRangeException>(() => BotIMarketMovementEvaluator.NoVigOver(1m, 2m));
}

static void CanonicalFixtureIdentityIsPortable()
{
    var sourceDate = new DateTime(2026, 9, 3, 20, 0, 0, DateTimeKind.Unspecified);
    var fixtureUtc = BotIShadowLab.ToUtcFromSantiago(sourceDate);
    var canonical = string.Join("|", fixtureUtc.ToString("O"), "LEAGUE", "HOME", "AWAY");
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    var expected = BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long))) & long.MaxValue;
    if (expected == 0) expected = 1;
    Equal(expected, BotIShadowLab.FixtureIdentity(sourceDate, " League ", "Home", "Away"));
}

static void StrongMoveApprovesOver()
{
    var result = Evaluate(BaseInput() with
    {
        OpeningLine = 2.5m,
        OpeningOverOdds = 2.10m,
        OpeningUnderOdds = 1.75m,
        CurrentLine = 3.5m,
        CurrentOverOdds = 1.70m,
        CurrentUnderOdds = 2.15m,
        PeerSource = "Pinnacle",
        PeerLine = 3.5m,
        PeerOverOdds = 1.65m,
        PeerUnderOdds = 2.25m
    });
    Equal(BotIShadowDecision.Approved, result.Decision);
    Equal("Over", result.Selection);
    True(result.SelectedProbabilityMovement > 0m);
    True(result.PinnacleOverNoVigProbability > result.BetanoOverNoVigProbability);
    ContainsItem(result.ReasonCodes, "SHADOW_MOVEMENT_SIGNAL_APPROVED");
}

static void StrongMoveApprovesUnder()
{
    var result = Evaluate(BaseInput() with
    {
        Source = "Pinnacle",
        OpeningLine = 3.5m,
        OpeningOverOdds = 1.70m,
        OpeningUnderOdds = 2.15m,
        CurrentLine = 2.5m,
        CurrentOverOdds = 2.15m,
        CurrentUnderOdds = 1.70m,
        PeerSource = "Betano",
        PeerLine = 2.5m,
        PeerOverOdds = 2.25m,
        PeerUnderOdds = 1.65m
    });
    Equal(BotIShadowDecision.Approved, result.Decision);
    Equal("Under", result.Selection);
    True(result.SelectedProbabilityMovement > 0m);
    True(result.SelectedLineMovement > 0m);
}

static void WeakMovementIsRejected()
{
    var result = Evaluate(BaseInput() with
    {
        OpeningOverOdds = 1.91m,
        OpeningUnderOdds = 1.91m,
        CurrentLine = 2.5m,
        CurrentOverOdds = 1.90m,
        CurrentUnderOdds = 1.92m,
        PeerSnapshotId = null,
        PeerSource = null,
        PeerCapturedAtUtc = null,
        PeerLine = null,
        PeerOverOdds = null,
        PeerUnderOdds = null
    });
    Equal(BotIShadowDecision.Rejected, result.Decision);
    ContainsItem(result.ReasonCodes, "MOVEMENT_BELOW_MINIMUM");
    ContainsItem(result.RiskFlags, "CROSS_BOOK_EVIDENCE_UNAVAILABLE");
}

static void MissingEvidenceAbstains()
{
    var tooSmall = Evaluate(BaseInput() with { SnapshotCount = 2 });
    Equal(BotIShadowDecision.Abstain, tooSmall.Decision);
    ContainsItem(tooSmall.ReasonCodes, "SNAPSHOT_SAMPLE_TOO_SMALL");

    var staleInput = BaseInput();
    var stale = Evaluate(staleInput with
    {
        OpeningCapturedAtUtc = staleInput.PredictionTimestampUtc.AddHours(-5),
        CurrentCapturedAtUtc = staleInput.PredictionTimestampUtc.AddHours(-3),
        PeerCapturedAtUtc = staleInput.PredictionTimestampUtc.AddHours(-3)
    });
    Equal(BotIShadowDecision.Abstain, stale.Decision);
    ContainsItem(stale.ReasonCodes, "CURRENT_ODDS_STALE");
}

static void FutureEvidenceAbstains()
{
    var value = BaseInput();
    var result = Evaluate(value with
    {
        CurrentCapturedAtUtc = value.PredictionTimestampUtc.AddMinutes(1)
    });
    Equal(BotIShadowDecision.Abstain, result.Decision);
    ContainsItem(result.ReasonCodes, "TEMPORAL_EVIDENCE_INVALID");
}

static void CrossBookContradictionRejects()
{
    var result = Evaluate(BaseInput() with
    {
        OpeningOverOdds = 2.00m,
        OpeningUnderOdds = 1.80m,
        CurrentOverOdds = 1.75m,
        CurrentUnderOdds = 2.05m,
        PeerSource = "Pinnacle",
        PeerLine = 2.5m,
        PeerOverOdds = 2.25m,
        PeerUnderOdds = 1.60m
    });
    Equal(BotIShadowDecision.Rejected, result.Decision);
    ContainsItem(result.ReasonCodes, "CROSS_BOOK_CONTRADICTION");
}

static void SnapshotIsHonest()
{
    var result = Evaluate(BaseInput());
    Contains(result.FeatureSnapshotJson, "\"shadowOnly\":true");
    Contains(result.FeatureSnapshotJson, "\"publicationBlocked\":true");
    Contains(result.FeatureSnapshotJson, "\"strictPointInTime\":true");
    Contains(result.FeatureSnapshotJson, "\"noOutcomeDataRead\":true");
    NotContains(result.FeatureSnapshotJson, "actualValue");
    NotContains(result.FeatureSnapshotJson, "profitLoss");
}

static void IdempotencyIsStable()
{
    var first = Evaluate(BaseInput());
    var firstKey = BotIShadowLab.BuildIdempotencyKey(first);
    Equal(firstKey, BotIShadowLab.BuildIdempotencyKey(first));
    var next = first with { CurrentSnapshotId = first.CurrentSnapshotId + 1 };
    NotEqual(firstKey, BotIShadowLab.BuildIdempotencyKey(next));
    Equal(64, firstKey.Length);
}

static void CollectorIsIdempotent()
{
    var now = DateTime.UtcNow;
    var localDate = DateTime.SpecifyKind(now.AddDays(1), DateTimeKind.Utc);
    var repository = new FakeRepository(new BotIMarketTimelineCandidate
    {
        SourceMatchDate = localDate,
        League = "League",
        HomeTeam = "Home",
        AwayTeam = "Away",
        Source = "Betano",
        SourceMarketType = "GoalsTotal",
        OpeningSnapshotId = 1,
        OpeningCapturedAtUtc = now.AddHours(-2),
        OpeningLine = 2.5m,
        OpeningOverOdds = 2.10m,
        OpeningUnderOdds = 1.75m,
        CurrentSnapshotId = 2,
        CurrentCapturedAtUtc = now.AddMinutes(-5),
        CurrentLine = 3.5m,
        CurrentOverOdds = 1.70m,
        CurrentUnderOdds = 2.15m,
        SnapshotCount = 3
    });
    var collector = new BotIShadowCollectorService(
        repository,
        new BotIMarketMovementEvaluator(),
        new BotIMarketMovementConfiguration());
    var command = new BotICollectCommand(
        DateOnly.FromDateTime(now),
        DateOnly.FromDateTime(now).AddDays(2),
        now,
        10);
    var first = collector.CollectAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    var second = collector.CollectAsync(command, CancellationToken.None).GetAwaiter().GetResult();
    Equal(1, first.Inserted);
    Equal(0, second.Inserted);
    Equal(1, second.AlreadyCaptured);
    Equal(1, repository.AppendCalls);
    Equal(1, repository.UniqueKeys.Count);
}

static void MigrationGuardsShadowOnly()
{
    var sql = Migration();
    Contains(sql, "CREATE TABLE dbo.BotI2026ShadowEvaluations");
    Contains(sql, "IX_CornerOddsSnapshots_BotIWindow");
    Contains(sql, "UX_BotI2026ShadowEvaluations_CurrentSnapshot");
    Contains(sql, "CONSTRAINT CK_BotI2026ShadowEvaluations_Shadow CHECK (ShadowOnly = 1 AND PublicationBlocked = 1)");
    Contains(sql, "trg_BotI2026ShadowEvaluations_Immutable");
    Contains(sql, "trg_AutomatedCornerBetSelections_BlockI2026");
    Contains(sql, "sp_AppendBotI2026ShadowEvaluation");
    Contains(sql, "WITH (UPDLOCK, HOLDLOCK)");
    Contains(sql, "idempotent replay changed decision evidence without a configuration-version bump");
    Contains(sql, "HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), @FeatureSnapshotJson))");
    Contains(sql, "history.ApiFootballFixtureId = shadow.ApiFootballFixtureId");
    Contains(sql, "history.ApiFootballUpdatedAtUtc <= shadow.PredictionTimestampUtc");
    Contains(sql, "history.ApiFootballUpdatedAtUtc > @AsOfUtc");
    Contains(sql, "PARTITION BY lab.FixtureIdentity, lab.ConfigurationVersion, lab.Decision");
    Contains(sql, "(N'Configuration', lab.ConfigurationVersion)");
    Contains(sql, "ApprovedSequence = 1 AND segment.SettlementState = N'Settled'");
    Contains(sql, "PromotionState = N'SHADOW_ONLY'");
    Contains(sql, "Deployable = CONVERT(BIT, 0)");
    Contains(sql, "OUTCOME_AWARE_SHADOW_OFFICIAL_FIXTURE_ONLY");
}

static void MigrationParses()
{
    var parser = new TSql160Parser(initialQuotedIdentifiers: true);
    using var reader = new StringReader(Migration());
    _ = parser.Parse(reader, out var errors);
    if (errors.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error =>
            $"L{error.Line},C{error.Column} SQL{error.Number}: {error.Message}")));
    }
}

static void TimelineSqlParses()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "CornersPrediction.Infrastructure",
        "SqlServer",
        "SqlServerBotIShadowRepository.cs"));
    const string openingMarker = "private const string TimelineSql = \"\"\"";
    const string closingMarker = "\n        \"\"\";";
    var start = source.IndexOf(openingMarker, StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException("Bot I timeline SQL opening marker was not found.");
    start += openingMarker.Length;
    var end = source.IndexOf(closingMarker, start, StringComparison.Ordinal);
    if (end < 0)
        throw new InvalidOperationException("Bot I timeline SQL closing marker was not found.");

    var sql = source[start..end];
    var parser = new TSql160Parser(initialQuotedIdentifiers: true);
    using var reader = new StringReader(sql);
    _ = parser.Parse(reader, out var errors);
    if (errors.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error =>
            $"L{error.Line},C{error.Column} SQL{error.Number}: {error.Message}")));
    }
    NotContains(sql, " AS current");
    Contains(sql, "INTO #BotISelectedFixtures");
    Contains(sql, "SWITCHOFFSET(snapshot.MatchDate AT TIME ZONE 'Pacific SA Standard Time', '+00:00')) > @AsOfUtc");
}

static void ApiSurfaceIsSafe()
{
    var controller = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "CornersPredictionApi", "Controllers", "BotI2026Controller.cs"));
    Contains(controller, "[HttpGet(\"status\")]");
    Contains(controller, "[HttpPost(\"collect\")]");
    Contains(controller, "[HttpGet(\"evaluations\")]");
    Contains(controller, "[HttpGet(\"scorecard\")]");
    NotContains(controller, "[HttpPost(\"publish");
    NotContains(controller, "[HttpPost(\"promote");
    NotContains(controller, "AutomatedCornerBetSelections");
}

static void AutomaticCollectorIsConfigured()
{
    var root = FindRepositoryRoot();
    var worker = File.ReadAllText(Path.Combine(root,
        "CornersPredictionApi", "Robot", "AutomatedCornersBot", "BotIShadowCollectorWorker.cs"));
    var program = File.ReadAllText(Path.Combine(root, "CornersPredictionApi", "Program.cs"));
    Contains(worker, "public bool Enabled { get; init; } = true;");
    Contains(worker, "public int PollMinutes { get; init; } = 15;");
    Contains(worker, "public int MaximumFixtures { get; init; } = 50;");
    Contains(worker, "IBotIShadowCollectorService");
    NotContains(worker, "AutomatedCornerBetSelections");
    Contains(program, "AddHostedService<BotIShadowCollectorWorker>()");
}

static BotIShadowEvaluationDraft Evaluate(BotIMarketMovementInput input) =>
    new BotIMarketMovementEvaluator().Evaluate(input, new BotIMarketMovementConfiguration());

static BotIMarketMovementInput BaseInput()
{
    var fixture = new DateTime(2026, 9, 2, 20, 0, 0, DateTimeKind.Utc);
    var prediction = fixture.AddHours(-2);
    return new BotIMarketMovementInput(
        42,
        1234,
        fixture,
        prediction,
        "League",
        "Home",
        "Away",
        "Betano",
        "source-1",
        "TotalGoals",
        10,
        prediction.AddHours(-2),
        2.5m,
        2.10m,
        1.75m,
        20,
        prediction.AddMinutes(-5),
        3.5m,
        1.70m,
        2.15m,
        4,
        30,
        "Pinnacle",
        prediction.AddMinutes(-7),
        3.5m,
        1.65m,
        2.25m);
}

static string Migration() => File.ReadAllText(Path.Combine(
    FindRepositoryRoot(),
    "CornersPredictionApi",
    "sql",
    "20260831_bot_i_shadow_market_movement.sql"));

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CornersPrediction.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static void Contains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected text was not found: {expected}");
}

static void ContainsItem(IEnumerable<string> values, string expected)
{
    if (!values.Contains(expected, StringComparer.Ordinal))
        throw new InvalidOperationException($"Expected item was not found: {expected}");
}

static void NotContains(string value, string unexpected)
{
    if (value.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected text was found: {unexpected}");
}

static void Near(decimal expected, decimal actual, decimal tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

static void NotEqual<T>(T left, T right) where T : notnull
{
    if (EqualityComparer<T>.Default.Equals(left, right))
        throw new InvalidOperationException($"Values unexpectedly matched: {left}.");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class FakeRepository : IBotIShadowRepository
{
    private readonly IReadOnlyList<BotIMarketTimelineCandidate> _timelines;
    public HashSet<string> UniqueKeys { get; } = new(StringComparer.Ordinal);
    public HashSet<long> CapturedSnapshots { get; } = [];
    public int AppendCalls { get; private set; }

    public FakeRepository(params BotIMarketTimelineCandidate[] timelines) => _timelines = timelines;

    public Task<IReadOnlyList<BotIMarketTimelineCandidate>> GetTimelinesAsync(
        BotICollectCommand command,
        CancellationToken cancellationToken) => Task.FromResult(_timelines);

    public Task<IReadOnlySet<long>> GetCapturedCurrentSnapshotIdsAsync(
        IReadOnlyCollection<long> currentSnapshotIds,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<long>>(
            CapturedSnapshots.Where(currentSnapshotIds.Contains).ToHashSet());

    public Task<bool> AppendAsync(BotIShadowEvaluationDraft evaluation, CancellationToken cancellationToken)
    {
        AppendCalls++;
        var inserted = UniqueKeys.Add(BotIShadowLab.BuildIdempotencyKey(evaluation));
        if (inserted) CapturedSnapshots.Add(evaluation.CurrentSnapshotId);
        return Task.FromResult(inserted);
    }

    public Task<BotIShadowStatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<BotIEvaluationPage> GetEvaluationsAsync(
        BotIEvaluationFilter filter,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<BotIShadowScorecardDto>> GetScorecardsAsync(
        DateTime? asOfUtc,
        string? configurationVersion,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
