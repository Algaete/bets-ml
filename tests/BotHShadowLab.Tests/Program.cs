using CornersPrediction.Application.Automation.BotH;
using CornersPrediction.Application.Automation.BotC;
using CornersPredictionApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.Json;

var tests = new (string Name, Action Run)[]
{
    ("Scorecard windows are fixed at 7/30/90", ScorecardWindowsAreFixed),
    ("Query contract rejects future as-of and invalid ranges", QueryContractIsFailClosed),
    ("Asian settlement handles full results and pushes", FullAsianSettlement),
    ("Asian settlement handles quarter-line half results", QuarterAsianSettlement),
    ("Settlement rejects unsupported inputs", SettlementRejectsInvalidInputs),
    ("Migration captures immutable temporal evidence", MigrationCapturesImmutableTemporalEvidence),
    ("Selector snapshot exposes the H lineage paths", SelectorSnapshotExposesLineagePaths),
    ("Migration parses with SQL Server ScriptDom", MigrationParsesWithScriptDom),
    ("Dynamic settlement is official, unique and temporal", DynamicSettlementIsFailClosed),
    ("Scorecards are shadow-only and economic", ScorecardsAreHonest),
    ("API and repository remain read-only", SurfaceIsReadOnly),
    ("Controller rejects a future as-of without querying", ControllerRejectsFutureAsOf),
    ("Controller exposes GET actions only", ControllerExposesGetOnly)
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

Console.WriteLine($"Bot H shadow-lab tests: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void ScorecardWindowsAreFixed() =>
    SequenceEqual(new[] { 7, 30, 90 }, BotHShadowLab.ScorecardWindows);

static void QueryContractIsFailClosed()
{
    var now = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.NormalizeAsOfUtc(now.AddMinutes(2), now));
    Throws<ArgumentException>(() => BotHShadowLab.Validate(
        new BotHShadowEvaluationFilter(now, now, now), now));
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.Validate(
        new BotHShadowEvaluationFilter(AsOfUtc: now, Page: 0), now));
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.Validate(
        new BotHShadowEvaluationFilter(AsOfUtc: now, PageSize: 1001), now));
    BotHShadowLab.Validate(
        new BotHShadowEvaluationFilter(now.AddDays(-1), now, now, Page: 1, PageSize: 1000), now);
}

static void FullAsianSettlement()
{
    Equal(new BotHSettlementResult("Win", 1m, 0.9m, 1m),
        BotHShadowLab.CalculateSettlement(11, 10.5m, "Over", 1.9m));
    Equal(new BotHSettlementResult("Loss", -1m, -1m, 0m),
        BotHShadowLab.CalculateSettlement(10, 10.5m, "Over", 1.9m));
    Equal(new BotHSettlementResult("Push", 0m, 0m, 1m / 1.9m),
        BotHShadowLab.CalculateSettlement(10, 10m, "Under", 1.9m));
    Equal(new BotHSettlementResult("Win", 1m, 0.9m, 1m),
        BotHShadowLab.CalculateSettlement(9, 10m, "Under", 1.9m));
}

static void QuarterAsianSettlement()
{
    Equal(new BotHSettlementResult("HalfWin", 0.5m, 0.45m, 1.45m / 1.9m),
        BotHShadowLab.CalculateSettlement(11, 10.75m, "Over", 1.9m));
    Equal(new BotHSettlementResult("HalfLoss", -0.5m, -0.5m, 0.5m / 1.9m),
        BotHShadowLab.CalculateSettlement(10, 10.25m, "Over", 1.9m));
    Equal(new BotHSettlementResult("HalfWin", 0.5m, 0.45m, 1.45m / 1.9m),
        BotHShadowLab.CalculateSettlement(10, 10.25m, "Under", 1.9m));
    Equal(new BotHSettlementResult("HalfLoss", -0.5m, -0.5m, 0.5m / 1.9m),
        BotHShadowLab.CalculateSettlement(11, 10.75m, "Under", 1.9m));
}

static void SettlementRejectsInvalidInputs()
{
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.CalculateSettlement(10, 10.1m, "Over", 1.9m));
    Throws<ArgumentException>(() => BotHShadowLab.CalculateSettlement(10, 10.5m, "OVER", 1.9m));
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.CalculateSettlement(10, 10.5m, "Over", 1m));
    Throws<ArgumentOutOfRangeException>(() => BotHShadowLab.CalculateSettlement(-1, 10.5m, "Over", 1.9m));
}

static void MigrationCapturesImmutableTemporalEvidence()
{
    var sql = Migration();
    Contains(sql, "CREATE TABLE dbo.BotH2026ShadowEvaluations");
    Contains(sql, "FeatureSnapshotHash BINARY(32) NOT NULL");
    Contains(sql, "OddsCapturedAtUtc <= PredictionTimestampUtc AND PredictionTimestampUtc < FixtureDateUtc");
    Contains(sql, "snapshot.CapturedAtUtc <= @PredictionTimestampUtc");
    Contains(sql, "snapshot.OverOdds = @EvaluationSelectedOdds");
    Contains(sql, "snapshot.UnderOdds = @EvaluationSelectedOdds");
    Contains(sql, "trg_BotH2026ShadowEvaluations_Immutable");
    Contains(sql, "AFTER UPDATE, DELETE");
    Contains(sql, "@Strict = 1");
}

static void SelectorSnapshotExposesLineagePaths()
{
    var fixtureUtc = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc);
    var predictionUtc = fixtureUtc.AddDays(-1);
    var history = Enumerable.Range(2, 20)
        .Select(days => new BotCHistoricalObservation(fixtureUtc.AddDays(-days), 4d, 4d))
        .ToArray();
    var input = new BotCPickEvaluationInput(
        "TotalCorners",
        9.5m,
        1.9m,
        1.9m,
        predictionUtc.AddMinutes(-5),
        fixtureUtc,
        8d,
        1d,
        "Models 2026",
        "test-v1",
        history,
        history.Take(10).ToArray(),
        history,
        history.Take(10).ToArray(),
        PredictionTimestampUtc: predictionUtc);
    var decision = new BotCPickDecisionEngine().Evaluate(input, new BotCStrategyConfiguration());
    using var document = JsonDocument.Parse(decision.FeatureSnapshotJson);
    var root = document.RootElement;
    Equal(predictionUtc, root.GetProperty("predictionTimestampUtc").GetDateTime());
    Equal(fixtureUtc, root.GetProperty("asOfDateUtc").GetDateTime());
    var market = root.GetProperty("market");
    Equal("TotalCorners", market.GetProperty("marketType").GetString()!);
    Equal("Under", market.GetProperty("selectedSide").GetString()!);
    Equal(9.5m, market.GetProperty("line").GetDecimal());
    Equal(1.9m, market.GetProperty("selectedOdds").GetDecimal());
    Equal(1.9m, market.GetProperty("oppositeOdds").GetDecimal());
}

static void MigrationParsesWithScriptDom()
{
    var parser = new TSql160Parser(initialQuotedIdentifiers: true);
    var root = FindRepositoryRoot();
    var scripts = new[]
    {
        "automated_corners_bot.sql",
        "20260819_bot_g2026.sql",
        "20260827_bot_h_shadow_lab.sql"
    };

    foreach (var script in scripts)
    {
        using var reader = new StringReader(File.ReadAllText(
            Path.Combine(root, "CornersPredictionApi", "sql", script)));
        _ = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
        {
            var rendered = string.Join(Environment.NewLine, errors.Select(error =>
                $"{script}: L{error.Line},C{error.Column} SQL{error.Number}: {error.Message}"));
            throw new InvalidOperationException(rendered);
        }
    }
}

static void DynamicSettlementIsFailClosed()
{
    var sql = Migration();
    Contains(sql, "CREATE OR ALTER FUNCTION dbo.fn_BotH2026ShadowLab");
    Contains(sql, "history.ApiFootballFixtureId = evidence.ApiFootballFixtureId");
    Contains(sql, "CandidateRank = DENSE_RANK()");
    Contains(sql, "WHEN outcome.MatchCandidateCount > 1 THEN N'Ambiguous'");
    Contains(sql, "ISNULL(outcome.ApiFootballCornersAvailable, 0) <> 1");
    Contains(sql, "outcome.ApiFootballUpdatedAtUtc <= outcome.PredictionTimestampUtc");
    Contains(sql, "outcome.ApiFootballUpdatedAtUtc > @AsOfUtc");
    Contains(sql, "WHEN outcome.SnapshotLineageState <> N'Valid' THEN N'SnapshotInvalid'");
    Contains(sql, "WHEN 0.5000 THEN N'HalfWin'");
    Contains(sql, "WHEN -0.5000 THEN N'HalfLoss'");
}

static void ScorecardsAreHonest()
{
    var sql = Migration();
    Contains(sql, "INSERT INTO @Windows(WindowDays) VALUES (7), (30), (90)");
    Contains(sql, "ApprovedSequence = 1 AND SettlementState = N'Settled'");
    Contains(sql, "FIRST_APPROVED_PER_FIXTURE_CONFIGURATION");
    Contains(sql, "EconomicOutcome");
    Contains(sql, "DeltaBrier = aggregated.Brier - aggregated.MarketBrier");
    Contains(sql, "Deployable = CONVERT(BIT, 0)");
    Contains(sql, "PromotionState = N'SHADOW_ONLY'");
    Contains(sql, "UnsafeOrUnavailable");
}

static void SurfaceIsReadOnly()
{
    var root = FindRepositoryRoot();
    var controller = File.ReadAllText(Path.Combine(
        root, "CornersPredictionApi", "Controllers", "BotH2026Controller.cs"));
    var repository = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Infrastructure", "SqlServer", "SqlServerBotHShadowLabRepository.cs"));

    Contains(controller, "[HttpGet(\"status\")]");
    Contains(controller, "[HttpGet(\"evaluations\")]");
    Contains(controller, "[HttpGet(\"scorecards\")]");
    NotContains(controller, "[HttpPost");
    NotContains(controller, "[HttpPut");
    NotContains(controller, "[HttpDelete");
    Contains(repository, "sp_GetBotH2026ShadowEvaluations");
    Contains(repository, "sp_GetBotH2026ShadowScorecards");
    NotContains(repository, "ExecuteAsync");
    NotContains(repository, "INSERT INTO");
    NotContains(repository, "UPDATE ");
    NotContains(repository, "DELETE ");
}

static void ControllerRejectsFutureAsOf()
{
    var repository = new FakeBotHRepository();
    var controller = new BotH2026Controller(
        repository,
        NullLogger<BotH2026Controller>.Instance);
    var result = controller.GetScorecards(
            DateTime.UtcNow.AddMinutes(5),
            null,
            CancellationToken.None)
        .GetAwaiter().GetResult();
    if (result is not BadRequestObjectResult)
        throw new InvalidOperationException("Future as-of should produce HTTP 400.");
    Equal(0, repository.Calls);
}

static void ControllerExposesGetOnly()
{
    var actions = typeof(BotH2026Controller)
        .GetMethods(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.DeclaredOnly)
        .Select(method => new
        {
            method.Name,
            Routes = method.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true)
                .Cast<HttpMethodAttribute>()
                .ToArray()
        })
        .Where(value => value.Routes.Length > 0)
        .ToArray();
    Equal(3, actions.Length);
    if (actions.SelectMany(action => action.Routes)
        .Any(route => route.HttpMethods.Any(method => method != "GET")))
        throw new InvalidOperationException("Bot H controller exposed a mutating HTTP method.");
}

static string Migration() => File.ReadAllText(Path.Combine(
    FindRepositoryRoot(), "CornersPredictionApi", "sql", "20260827_bot_h_shadow_lab.sql"));

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

static void NotContains(string value, string unexpected)
{
    if (value.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected text was found: {unexpected}");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException("Sequences differ.");
}

static void Throws<TException>(Action action) where TException : Exception
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

sealed class FakeBotHRepository : IBotHShadowLabReadRepository
{
    public int Calls { get; private set; }

    public Task<BotHShadowLabStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new BotHShadowLabStatusDto());
    }

    public Task<BotHShadowEvaluationPage> GetEvaluationsAsync(
        BotHShadowEvaluationFilter filter,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new BotHShadowEvaluationPage([], 0, filter.Page, filter.PageSize, DateTime.UtcNow));
    }

    public Task<IReadOnlyList<BotHShadowScorecardDto>> GetScorecardsAsync(
        BotHShadowScorecardFilter filter,
        CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<BotHShadowScorecardDto>>([]);
    }
}
