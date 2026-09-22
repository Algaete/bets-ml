using System.Net;
using System.Net.Http.Json;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Controllers;
using CornersPrediction.Web.Models.BotPicks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

var tests = new (string Name, Action Run)[]
{
    ("Client sends the versioned invariant what-if query", ClientBuildsInvariantQuery),
    ("Controller returns the independent threshold partial", ControllerReturnsIndependentPartial),
    ("Invalid thresholds fail before calling the API", InvalidThresholdsDoNotCallApi),
    ("Threshold API failure stays inside its own block", ApiFailureIsIsolated),
    ("Razor surface remains read-only and split-aware", RazorSurfaceIsReadOnlyAndSplitAware),
    ("Evidence distinguishes a query failure from zero samples", EvidenceDistinguishesUnavailableFromZero),
    ("Evidence counts independent approvals and warns about mixed versions", EvidenceUsesIndependentApprovals),
    ("Brier comparison uses only verified paired evidence", BrierUsesPairedEvidence)
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

Console.WriteLine($"Bot H2026 Web tests: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void ClientBuildsInvariantQuery()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal(HttpMethod.Get, request.Method);
        Equal("/api/bot-h2026/threshold-analysis", request.RequestUri?.AbsolutePath);
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        Contains(query, "configurationVersion=cfg / 1");
        Contains(query, "marketType=HomeTeamCorners");
        Contains(query, "selection=Under");
        Contains(query, "analysisVersion=bot-h-threshold-what-if-1.0.0");
        Contains(query, "minimumFinalProbability=0.61");
        Contains(query, "minimumFinalEdge=0.07");
        Contains(query, "minimumFinalExpectedValue=0.05");
        Contains(query, "minimumDataQualityScore=0.8");
        Contains(query, "minimumContextAgreementScore=0.75");
        Contains(query, "minimumOdds=1.7");
        Contains(query, "maximumOdds=2.4");
        Contains(query, "developmentFraction=0.65");
        Contains(query, "asOfUtc=2026-08-31T12:30:00.0000000Z");
        IReadOnlyList<BotH2026ThresholdAnalysisViewModel> rows =
        [
            new() { Split = "Overall", SelectedPicks = 20 },
            new() { Split = "Development", SelectedPicks = 14 },
            new() { Split = "Holdout", SelectedPicks = 6 }
        ];
        return Json(HttpStatusCode.OK, rows);
    })) { BaseAddress = new Uri("http://h-web-tests") };

    var result = new BotH2026ApiClient(httpClient).GetThresholdAnalysisAsync(
        new BotH2026ThresholdAnalysisFiltersViewModel
        {
            AsOfUtc = new DateTime(2026, 8, 31, 12, 30, 0, DateTimeKind.Utc),
            ConfigurationVersion = "cfg / 1",
            MarketType = "HomeTeamCorners",
            Selection = "Under",
            MinimumFinalProbability = 0.61m,
            MinimumFinalEdge = 0.07m,
            MinimumFinalExpectedValue = 0.05m,
            MinimumDataQualityScore = 0.80m,
            MinimumContextAgreementScore = 0.75m,
            MinimumOdds = 1.70m,
            MaximumOdds = 2.40m,
            DevelopmentFraction = 0.65m
        },
        CancellationToken.None).GetAwaiter().GetResult();

    Equal(1, calls);
    Equal(3, result.Count);
    Equal("Holdout", result[2].Split);
}

static void ControllerReturnsIndependentPartial()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        IReadOnlyList<BotH2026ThresholdAnalysisViewModel> rows =
        [new() { Split = "Overall", ReadOnly = true, PromotionState = "SHADOW_ONLY" }];
        return Json(HttpStatusCode.OK, rows);
    })) { BaseAddress = new Uri("http://h-web-tests") };
    var controller = Controller(httpClient);

    var action = controller.ThresholdAnalysis(
            new BotH2026ThresholdAnalysisFiltersViewModel(),
            CancellationToken.None)
        .GetAwaiter().GetResult();
    var partial = action as PartialViewResult
        ?? throw new InvalidOperationException("Expected a partial view result.");
    Equal("_ThresholdAnalysis", partial.ViewName);
    var model = partial.Model as BotH2026IndexViewModel
        ?? throw new InvalidOperationException("Expected the H2026 index view model.");
    Check(model.ThresholdAnalysisRequested, "Analysis should be marked as requested.");
    Check(model.ThresholdAnalysisAvailable, "Analysis should be available.");
    Equal(1, model.ThresholdAnalysis.Count);
    Equal(1, calls);
}

static void InvalidThresholdsDoNotCallApi()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(_ =>
    {
        calls++;
        return Json(HttpStatusCode.OK, Array.Empty<BotH2026ThresholdAnalysisViewModel>());
    })) { BaseAddress = new Uri("http://h-web-tests") };
    var controller = Controller(httpClient);

    var action = controller.ThresholdAnalysis(
            new BotH2026ThresholdAnalysisFiltersViewModel
            {
                MinimumOdds = 2.50m,
                MaximumOdds = 1.50m
            },
            CancellationToken.None)
        .GetAwaiter().GetResult();
    var model = ModelFrom(action);
    Check(!model.ThresholdAnalysisAvailable, "Invalid odds should be shown in the isolated block.");
    Contains(model.ThresholdAnalysisErrorMessage ?? string.Empty, "rango de cuotas");
    Equal(0, calls);
}

static void ApiFailureIsIsolated()
{
    using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(
        HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("threshold query failed")
    })) { BaseAddress = new Uri("http://h-web-tests") };
    var controller = Controller(httpClient);

    var action = controller.ThresholdAnalysis(
            new BotH2026ThresholdAnalysisFiltersViewModel(),
            CancellationToken.None)
        .GetAwaiter().GetResult();
    var model = ModelFrom(action);
    Check(!model.ThresholdAnalysisAvailable, "The threshold block should report its API failure.");
    Contains(model.ThresholdAnalysisErrorMessage ?? string.Empty, "API HTTP 500");
    Check(model.EvaluationsAvailable && model.ScorecardsAvailable,
        "A what-if failure must not mark table or scorecards as failed.");
}

static void RazorSurfaceIsReadOnlyAndSplitAware()
{
    var root = FindRepositoryRoot();
    var index = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Web", "Views", "BotH2026", "Index.cshtml"));
    var partial = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Web", "Views", "BotH2026", "_ThresholdAnalysis.cshtml"));
    Contains(index, "id=\"HThresholdAnalysisHost\"");
    Contains(index, "js-h-threshold-form");
    Contains(index, "La tabla y los scorecards siguen disponibles");
    Contains(partial, "SHADOW_ONLY");
    Contains(partial, "Sólo lectura");
    Contains(partial, "Development");
    Contains(partial, "Holdout");
    Contains(partial, "SelectedPicks");
    Contains(partial, "CalibrationGap");
    Contains(partial, "DeltaBrier");
    Contains(partial, "todavía no es una validación independiente");
    Contains(partial, "Local y visita");
    Check(!partial.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase),
        "The threshold form must not expose a mutation method.");
}

static void EvidenceDistinguishesUnavailableFromZero()
{
    var unavailable = BotH2026EvidenceSummary.From(new() { ScorecardsErrorMessage = "Timeout" });
    Contains(unavailable.Headline, "no disponible");
    Contains(unavailable.Detail, "No se puede concluir");
    var empty = BotH2026EvidenceSummary.From(new()
    {
        Scorecards = [new() { WindowDays = 30, Dimension = "Overall", Evaluations = 0 }]
    });
    Contains(empty.Headline, "Sin capturas");
    var pending = BotH2026EvidenceSummary.From(new()
    {
        Scorecards = [new() { WindowDays = 30, Dimension = "Overall", Evaluations = 2000, ApprovedSignals = 200, Approved = 2 }]
    });
    Contains(pending.Headline, "Faltan resultados");
    Contains(pending.Detail, "2 primeras aprobaciones");
    Check(!pending.Detail.Contains("0%", StringComparison.Ordinal), "Missing settlements must not imply zero yield.");
}

static void EvidenceUsesIndependentApprovals()
{
    var model = new BotH2026IndexViewModel
    {
        Scorecards =
        [
            new() { WindowDays = 30, Dimension = "Overall", Evaluations = 1000, ApprovedSignals = 100, Approved = 3, SafelySettled = 2, ProfitLoss = 1 },
            new() { WindowDays = 30, Dimension = "Configuration", ConfigurationVersion = "v1", Evaluations = 700, Approved = 2, SafelySettled = 1 },
            new() { WindowDays = 30, Dimension = "Configuration", ConfigurationVersion = "v2", Evaluations = 300, Approved = 1, SafelySettled = 1 }
        ]
    };
    var summary = BotH2026EvidenceSummary.From(model);
    Check(summary.MixedConfigurations, "Multiple versions must not be treated as independent fixtures.");
    Contains(summary.Headline, "por separado");
    Contains(summary.Detail, "3 primeras aprobaciones");
    Contains(summary.NextStep, "mismo partido");
    Equal(2, summary.Configurations.Count);
}

static void BrierUsesPairedEvidence()
{
    var oldApi = new BotH2026ScorecardViewModel { DeltaBrier = -0.50, Brier = 0.01, MarketBrier = 0.51 };
    Check(oldApi.VerifiedDeltaBrier is null, "Aggregate Brier values with unknown cohorts must not imply a verified advantage.");
    var paired = new BotH2026ScorecardViewModel
    {
        DeltaBrier = -0.50, Brier = 0.01, MarketBrier = 0.51,
        PairedSamples = 2, PairedModelBrier = 0.25, PairedMarketBrier = 0.20
    };
    Check(Math.Abs(paired.VerifiedDeltaBrier!.Value - 0.05) < 0.000001,
        "Paired comparison must show the model is worse even if the unmatched aggregate appears better.");
    Check(BotH2026EvidenceSummary.PairedDelta(0, 0.1, 0.2) is null, "Zero pairs must remain unavailable.");
    Check(BotH2026EvidenceSummary.PairedDelta(5, double.NaN, 0.2) is null, "Invalid numbers cannot be interpreted.");
    Check(BotH2026EvidenceSummary.PairedDelta(5, 0.1, null) is null, "Missing market evidence cannot be compared.");
}

static BotH2026Controller Controller(HttpClient httpClient) => new(
    new BotH2026ApiClient(httpClient),
    NullLogger<BotH2026Controller>.Instance);

static BotH2026IndexViewModel ModelFrom(IActionResult action)
{
    var partial = action as PartialViewResult
        ?? throw new InvalidOperationException($"Expected partial view; got {action.GetType().Name}.");
    return partial.Model as BotH2026IndexViewModel
        ?? throw new InvalidOperationException("Expected BotH2026IndexViewModel.");
}

static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T value) => new(statusCode)
{
    Content = JsonContent.Create(value)
};

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

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
