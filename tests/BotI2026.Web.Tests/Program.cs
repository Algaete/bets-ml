using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Controllers;
using CornersPrediction.Web.Models.BotPicks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

var tests = new (string Name, Action Run)[]
{
    ("Client uses server paging and invariant I2026 filters", ClientUsesServerPaging),
    ("Collection calls only the shadow endpoint", CollectionUsesShadowEndpoint),
    ("Dashboard components fail independently", DashboardComponentsFailIndependently),
    ("Invalid collection is rejected before the API", InvalidCollectionDoesNotCallApi),
    ("Collection is protected against request forgery", CollectionRequiresAntiforgery),
    ("Razor surface is responsive, sortable and explicitly non-productive", RazorSurfaceIsSafeAndResponsive),
    ("Approved is labelled as shadow and never as permission to bet", ApprovedIsExplicitlyShadowOnly),
    ("Approved badge has explicit accessible contrast", ApprovedBadgeHasExplicitContrast)
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

Console.WriteLine($"Bot I2026 Web tests: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void ClientUsesServerPaging()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal(HttpMethod.Get, request.Method);
        Equal("/api/bot-i2026/evaluations", request.RequestUri?.AbsolutePath);
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        Contains(query, "predictionFromUtc=2026-08-01T00:00:00.0000000Z");
        Contains(query, "predictionToUtc=2026-09-01T00:00:00.0000000Z");
        Contains(query, "decision=Approved");
        Contains(query, "marketType=TotalGoals");
        Contains(query, "selection=Over");
        Contains(query, "source=Pinnacle");
        Contains(query, "configurationVersion=cfg / i");
        Contains(query, "page=3");
        Contains(query, "pageSize=50");
        return Json(HttpStatusCode.OK, new BotI2026EvaluationPageViewModel
        {
            Items = [new() { ShadowEvaluationId = 19 }],
            TotalRows = 120,
            Page = 3,
            PageSize = 50
        });
    })) { BaseAddress = new Uri("http://i-web-tests") };

    var result = new BotI2026ApiClient(httpClient).GetEvaluationsAsync(
        new BotI2026FiltersViewModel
        {
            PredictionFromUtc = new DateTime(2026, 8, 1),
            PredictionToUtc = new DateTime(2026, 9, 1),
            Decision = "Approved",
            MarketType = "TotalGoals",
            Selection = "Over",
            Source = "Pinnacle",
            ConfigurationVersion = "cfg / i",
            Page = 3,
            PageSize = 50
        },
        CancellationToken.None).GetAwaiter().GetResult();

    Equal(1, calls);
    Equal(120L, result.TotalRows);
    Equal(3, result.TotalPages);
}

static void CollectionUsesShadowEndpoint()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal(HttpMethod.Post, request.Method);
        Equal("/api/bot-i2026/collect", request.RequestUri?.AbsolutePath);
        Check(!request.RequestUri!.AbsolutePath.Contains("publish", StringComparison.OrdinalIgnoreCase),
            "The Web client must never call a publication route.");
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        Contains(body, "2026-08-31");
        Contains(body, "maximumFixtures");
        return Json(HttpStatusCode.OK, new BotI2026CollectResultViewModel
        {
            Inserted = 5,
            AlreadyCaptured = 2,
            ShadowOnly = true,
            PublicationBlocked = true
        });
    })) { BaseAddress = new Uri("http://i-web-tests") };

    var result = new BotI2026ApiClient(httpClient).CollectAsync(
        new BotI2026CollectViewModel
        {
            DateFrom = new DateOnly(2026, 8, 31),
            DateTo = new DateOnly(2026, 9, 8),
            MaximumFixtures = 250
        },
        CancellationToken.None).GetAwaiter().GetResult();

    Equal(1, calls);
    Equal(5, result.Inserted);
    Check(result.ShadowOnly && result.PublicationBlocked, "Collection must report both safety guards.");
}

static void DashboardComponentsFailIndependently()
{
    var scenarios = new[] { "status", "evaluations", "scorecards" };
    foreach (var failing in scenarios)
    {
        using var httpClient = new HttpClient(new StubHandler(request => ResponseFor(request, failing)))
        {
            BaseAddress = new Uri("http://i-web-tests")
        };
        var controller = Controller(httpClient);
        var filters = new BotI2026FiltersViewModel();

        var status = ModelFrom(controller.Status(CancellationToken.None).GetAwaiter().GetResult());
        var evaluations = ModelFrom(controller.Evaluations(filters, CancellationToken.None).GetAwaiter().GetResult());
        var scorecards = ModelFrom(controller.Scorecards(filters, CancellationToken.None).GetAwaiter().GetResult());

        Equal(failing == "status", !status.StatusAvailable);
        Equal(failing == "evaluations", !evaluations.EvaluationsAvailable);
        Equal(failing == "scorecards", !scorecards.ScorecardsAvailable);
    }
}

static void InvalidCollectionDoesNotCallApi()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(_ =>
    {
        calls++;
        return Json(HttpStatusCode.OK, new BotI2026CollectResultViewModel());
    })) { BaseAddress = new Uri("http://i-web-tests") };
    var result = Controller(httpClient).Collect(
            new BotI2026CollectViewModel
            {
                DateFrom = new DateOnly(2026, 8, 1),
                DateTo = new DateOnly(2026, 9, 1),
                MaximumFixtures = 250
            },
            CancellationToken.None)
        .GetAwaiter().GetResult();

    Check(result is BadRequestObjectResult, "A window larger than 14 days must return HTTP 400.");
    Equal(0, calls);
}

static void CollectionRequiresAntiforgery()
{
    var method = typeof(BotI2026Controller).GetMethod(nameof(BotI2026Controller.Collect))
        ?? throw new InvalidOperationException("Collect action was not found.");
    Check(method.GetCustomAttribute<HttpPostAttribute>() is not null,
        "Collection must remain a POST action.");
    Check(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null,
        "Collection must validate the antiforgery token.");
}

static void RazorSurfaceIsSafeAndResponsive()
{
    var root = FindRepositoryRoot();
    var index = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "Index.cshtml"));
    var evaluations = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "_Evaluations.cshtml"));
    var scorecards = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "_Scorecards.cshtml"));
    var layout = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "Shared", "_Layout.cshtml"));

    Contains(index, "id=\"IStatusHost\"");
    Contains(index, "id=\"IScorecardsHost\"");
    Contains(index, "id=\"IEvaluationsHost\"");
    Contains(index, "Recolectar shadow");
    Contains(index, "@Html.AntiForgeryToken()");
    Contains(index, "nunca se convierten en picks");
    Contains(index, "overflow: auto");
    Contains(evaluations, "Paginación desde SQL");
    Contains(evaluations, "asp-route-Page");
    Contains(evaluations, "js-i-sort");
    Contains(scorecards, "outcome-aware 7 / 30 / 90");
    Contains(scorecards, "ScorecardType");
    Contains(layout, "asp-controller=\"BotI2026\"");
    Contains(layout, "Shadow · movimiento");
    Check(!index.Contains("liquidar", StringComparison.OrdinalIgnoreCase),
        "I2026 must not expose a settlement command.");
    Check(!index.Contains("asp-action=\"Publish", StringComparison.OrdinalIgnoreCase)
          && !index.Contains("js-i-publish", StringComparison.OrdinalIgnoreCase),
        "I2026 must not expose a publication command.");
}

static void ApprovedIsExplicitlyShadowOnly()
{
    var root = FindRepositoryRoot();
    var evaluations = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "_Evaluations.cshtml"));

    Contains(evaluations, "Approved · SHADOW");
    Contains(evaluations, "No apostar");
    Contains(evaluations, "Approved sólo significa que la señal pasó las reglas del laboratorio");
    Contains(evaluations, "no autoriza una apuesta real");
    Check(evaluations.Contains("item.PublicationBlocked", StringComparison.Ordinal),
        "The audit row must continue displaying the publication barrier for every shadow decision.");
}

static void ApprovedBadgeHasExplicitContrast()
{
    var root = FindRepositoryRoot();
    var index = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "Index.cshtml"));
    var evaluations = File.ReadAllText(Path.Combine(root, "CornersPrediction.Web", "Views", "BotI2026", "_Evaluations.cshtml"));

    Contains(evaluations, "i-decision-approved");
    Contains(index, ".i-decision-approved");
    Contains(index, "color:");
    Contains(index, "background:");
    Check(!evaluations.Contains("\"Approved\" => \"text-bg-info\"", StringComparison.Ordinal),
        "Approved must not depend on Bootstrap's contextual badge contrast in the current theme.");
}

static HttpResponseMessage ResponseFor(HttpRequestMessage request, string failComponent)
{
    var path = request.RequestUri?.AbsolutePath ?? string.Empty;
    var component = path.EndsWith("/status", StringComparison.Ordinal)
        ? "status"
        : path.EndsWith("/scorecards", StringComparison.Ordinal)
            ? "scorecards"
            : "evaluations";
    if (component == failComponent)
        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
    if (component == "status")
        return Json(HttpStatusCode.OK, new BotI2026StatusViewModel { SchemaReady = true });
    if (component == "scorecards")
        return Json(HttpStatusCode.OK, (IReadOnlyList<BotI2026ScorecardViewModel>)[new() { WindowDays = 7 }]);
    return Json(HttpStatusCode.OK, new BotI2026EvaluationPageViewModel { TotalRows = 1 });
}

static BotI2026Controller Controller(HttpClient httpClient) => new(
    new BotI2026ApiClient(httpClient),
    NullLogger<BotI2026Controller>.Instance);

static BotI2026IndexViewModel ModelFrom(IActionResult action)
{
    var partial = action as PartialViewResult
        ?? throw new InvalidOperationException($"Expected partial view; got {action.GetType().Name}.");
    return partial.Model as BotI2026IndexViewModel
        ?? throw new InvalidOperationException("Expected BotI2026IndexViewModel.");
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
