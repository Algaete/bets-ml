using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Controllers;
using CornersPrediction.Web.Models.BotPicks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Contains("--live", StringComparer.Ordinal))
    return await RunLiveTwoPhaseComparison();
if (args.Contains("--live-general", StringComparer.Ordinal))
    return await RunLiveGeneralPicks();
if (args.Contains("--live-lab", StringComparer.Ordinal))
    return await RunLiveGeneralPicksLab();

var tests = new (string Name, Action Run)[]
{
    ("Research client sends every independent filter", ClientSendsResearchFilters),
    ("Manual settlement uses admin/CSRF and forwards the authenticated actor", ManualSettlementUsesAuthenticatedActor),
    ("Selection queries constrain current, older and canonical rows to each market", SelectionQueriesUseMarketFamily),
    ("Monthly history uses aggregate API without downloading selections", MonthlyHistoryUsesAggregateApi),
    ("Initial table preserves every market row with explicit unverified zero-stake plans", InitialSelectionsAreVisibleAndUnverified),
    ("Bot Picks controller normalizes and proxies the research page", ControllerProxiesResearchPage),
    ("General picks keep every scope and status unless an explicit filter is selected", GeneralPicksPreserveAllStatuses),
    ("Approved lab uses the current slice without paging or decision overrides", ApprovedLabUsesCurrentSlice),
    ("Research UI keeps scientific and production decisions separate", ResearchUiIsExplicitlySeparated)
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

Console.WriteLine($"Automated Bot Research Web tests: {tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static void ManualSettlementUsesAuthenticatedActor()
{
    var method = typeof(BotPicksController).GetMethod(nameof(BotPicksController.SettleGeneralPick))!;
    Check(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).Length == 1, "Manual writes need CSRF protection.");
    Check(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
        .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Any(attribute => attribute.Policy == CornersPrediction.Web.Services.PlatformPolicies.Admin), "Manual writes require admin.");
    using var http = new HttpClient(new StubHandler(request =>
    {
        Equal(HttpMethod.Put, request.Method, "manual method");
        Equal("/api/automated-corners/general-picks/42/settlement", request.RequestUri!.AbsolutePath, "manual path");
        Equal("operator@example.test", request.Headers.GetValues("X-Acting-User").Single(), "authenticated actor");
        var body = request.Content!.ReadFromJsonAsync<GeneralPickManualSettlementViewModel>().GetAwaiter().GetResult()!;
        Equal(0, body.ActualValue, "zero outcome");
        Equal("Official league site", body.Reason, "audit note");
        return Json(HttpStatusCode.OK, new { saved = true });
    })) { BaseAddress = new Uri("http://manual-test") };
    var controller = new BotPicksController(new AutomatedCornersApiClient(http), new RecommendationAutomationApiClient(http), NullLogger<BotPicksController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                new(System.Security.Claims.ClaimTypes.Name, "operator@example.test"),
                new(System.Security.Claims.ClaimTypes.Role, "Admin")], "test")) } }
    };
    Check(controller.SettleGeneralPick(42, new(0, "Official league site", Guid.NewGuid()), default).GetAwaiter().GetResult() is OkObjectResult,
        "Manual settlement proxy failed.");
}

static async Task<int> RunLiveTwoPhaseComparison()
{
    var values = File.ReadLines(Path.Combine(FindRepositoryRoot(), ".env"))
        .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"', '\''));
    using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5070"), Timeout = TimeSpan.FromMinutes(2) };
    httpClient.DefaultRequestHeaders.Add("X-Internal-Api-Key", values["INTERNAL_API_KEY"]);
    var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(httpClient), NullLogger<BotPicksController>.Instance);
    foreach (var family in new[] { "goals", "corners", "shots", "sog" })
    {
        var filters = new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            DateTo = DateTime.Today
        };
        var stopwatch = Stopwatch.StartNew();
        var first = await controller.Selections(filters, family, includePerformance: false) as JsonResult
            ?? throw new InvalidOperationException($"Initial {family} request did not return selections.");
        var initialMs = stopwatch.ElapsedMilliseconds;
        var initial = (IReadOnlyList<BotPickSelectionViewModel>)first.Value!;
        Check(initial.All(row => row.ProductionPlan is { Key: "verifying", StakeUnits: 0m, IsProductive: false }),
            "Unverified live rows must have no authorized stake.");
        stopwatch.Restart();
        var final = await controller.Selections(filters, family) as JsonResult
            ?? throw new InvalidOperationException($"Full {family} verification failed.");
        var fullMs = stopwatch.ElapsedMilliseconds;
        var verified = (IReadOnlyList<BotPickSelectionViewModel>)final.Value!;
        Check(initial.Select(row => row.AutomatedCornerBetSelectionId).Order().SequenceEqual(
            verified.Select(row => row.AutomatedCornerBetSelectionId).Order()),
            $"{family}: two phases must preserve exactly the same selection IDs.");
        Console.WriteLine($"LIVE {family}: initialMs={initialMs}; fullValidationMs={fullMs}; identicalRows={initial.Count}; unverifiedStake=0");
    }
    return 0;
}

static async Task<int> RunLiveGeneralPicks()
{
    var values = File.ReadLines(Path.Combine(FindRepositoryRoot(), ".env"))
        .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"', '\''));
    using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5070"), Timeout = TimeSpan.FromSeconds(40) };
    httpClient.DefaultRequestHeaders.Add("X-Internal-Api-Key", values["INTERNAL_API_KEY"]);
    var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(httpClient), NullLogger<BotPicksController>.Instance);
    foreach (var family in new[] { "goals", "corners", "shots", "sog" })
    {
        var filters = new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            DateTo = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(-1),
            MarketFamily = family, PageSize = 50
        };
        var watch = Stopwatch.StartNew();
        var result = await controller.GeneralPicks(filters) as JsonResult
            ?? throw new InvalidOperationException($"General {family} request failed.");
        var page = (BotResearchEvaluationPageViewModel)result.Value!;
        Check(page.Items.Count <= 50, "General pages must stay bounded.");
        Check(page.Items.Select(row => row.EvaluationId).Distinct().Count() == page.Items.Count, "General page IDs must not repeat.");
        Check(page.AvailableBots.Count > 0, "General bot catalog must round-trip through the Web client.");
        Console.WriteLine($"LIVE general {family}: elapsedMs={watch.ElapsedMilliseconds}; total={page.TotalCount}; pageRows={page.Items.Count}; marketScopes={string.Join(',', page.Items.Select(row => row.MarketType).Distinct())}; statuses={string.Join(',', page.Items.Select(row => row.PublicationStatus).Distinct())}");
        if (family == "goals")
        {
            foreach (var scope in new[] { "TotalGoals", "HomeTeamGoals", "AwayTeamGoals" })
            {
                filters.MarketType = scope;
                var scoped = await controller.GeneralPicks(filters) as JsonResult
                    ?? throw new InvalidOperationException($"General {scope} request failed.");
                var scopedPage = (BotResearchEvaluationPageViewModel)scoped.Value!;
                Check(scopedPage.Items.All(row => row.MarketType == scope), "Explicit market filter must match every row.");
                Console.WriteLine($"LIVE general {scope}: total={scopedPage.TotalCount}; pageRows={scopedPage.Items.Count}");
            }
            filters.MarketType = null;
            filters.PublicationStatus = "ProductionBlocked";
            var blocked = await controller.GeneralPicks(filters) as JsonResult
                ?? throw new InvalidOperationException("General blocked request failed.");
            var blockedPage = (BotResearchEvaluationPageViewModel)blocked.Value!;
            Check(blockedPage.Items.All(row => row.PublicationStatus == "ProductionBlocked"), "Explicit blocked filter must match every row.");
            Check(blockedPage.TotalCount <= page.TotalCount, "Blocked results must be a subset of general results.");
            Console.WriteLine($"LIVE explicit blocked goals: total={blockedPage.TotalCount}; pageRows={blockedPage.Items.Count}");
        }
    }
    return 0;
}

static async Task<int> RunLiveGeneralPicksLab()
{
    var values = File.ReadLines(Path.Combine(FindRepositoryRoot(), ".env"))
        .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim().Trim('"', '\''));
    using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5070"), Timeout = TimeSpan.FromMinutes(3) };
    httpClient.DefaultRequestHeaders.Add("X-Internal-Api-Key", values["INTERNAL_API_KEY"]);
    var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(httpClient), NullLogger<BotPicksController>.Instance);
    foreach (var family in new[] { "goals", "corners", "shots", "sog" })
    {
        var filters = new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            DateTo = DateTime.Today,
            MarketFamily = family,
            ModelDecision = "Rejected"
        };
        var watch = Stopwatch.StartNew();
        var action = await controller.GeneralPicksLab(filters);
        if (action is not JsonResult json || json.Value is not GeneralPickLabViewModel lab)
            throw new InvalidOperationException($"Live lab {family} returned {action.GetType().Name}.");
        var coldMs = watch.ElapsedMilliseconds;
        Check(lab.Summary.ApprovedEvaluations >= lab.Summary.IndependentSignals,
            $"{family}: lab deduplication increased the population.");
        Check(lab.Summary.IndependentSignals == lab.Summary.ResolvedSignals + lab.Summary.PendingSignals
            + lab.Summary.UnavailableSignals + lab.Summary.VoidSignals,
            $"{family}: outcomes do not reconcile.");
        if (lab.Timeline.Count > 0)
            Equal(lab.Summary.ProfitLossUnits, lab.Timeline[^1].CumulativeProfitLossUnits,
                $"{family}: cumulative P/L");
        watch.Restart();
        var cached = await controller.GeneralPicksLab(filters);
        var cachedMs = watch.ElapsedMilliseconds;
        Check(cached is JsonResult, $"{family}: cached lab failed.");
        Console.WriteLine($"LIVE lab {family}: coldMs={coldMs}; cachedMs={cachedMs}; approved={lab.Summary.ApprovedEvaluations}; independent={lab.Summary.IndependentSignals}; resolved={lab.Summary.ResolvedSignals}; pending={lab.Summary.PendingSignals}; unavailable={lab.Summary.UnavailableSignals}; pnl={lab.Summary.ProfitLossUnits:0.####}; yield={lab.Summary.Yield:0.####}; bins={lab.Calibration.Count}; segments={lab.Segments.Count}");
    }
    return 0;
}

static void SelectionQueriesUseMarketFamily()
{
    foreach (var family in new[] { "corners", "goals", "shots", "sog" })
    {
        var selectionQueries = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/automated-corners/selections")
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                Contains(query, $"marketFamily={family.ToUpperInvariant()}");
                selectionQueries.Add(query);
                return Json(HttpStatusCode.OK, Array.Empty<BotPickSelectionViewModel>());
            }
            if (request.RequestUri?.AbsolutePath == "/api/automated-corners/performance/scorecards")
                return Json(HttpStatusCode.OK, Array.Empty<BotPerformanceScorecardViewModel>());
            throw new InvalidOperationException($"Unexpected endpoint {request.RequestUri}.");
        })) { BaseAddress = new Uri("http://selection-filter-tests") };
        using var definitionClient = new HttpClient(new StubHandler(_ =>
            Json(HttpStatusCode.OK, Array.Empty<object>())))
        { BaseAddress = new Uri("http://definition-tests") };
        var controller = new BotPicksController(
            new AutomatedCornersApiClient(httpClient),
            new RecommendationAutomationApiClient(definitionClient),
            NullLogger<BotPicksController>.Instance);
        var result = controller.Selections(new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1),
            DateTo = new DateTime(2026, 9, 6),
            Bookmaker = "Pinnacle"
        }, family).GetAwaiter().GetResult();
        Check(result is JsonResult, $"{family} selections should load.");
        Equal(family == "goals" ? 3 : 2, selectionQueries.Count, $"{family} calls");
        Check(selectionQueries.Any(query => query.Contains("dateTo=2026-08-31")
            && query.Contains("onlyPending=true")), $"{family}: older pending rows remain visible.");
        if (family == "goals")
            Check(selectionQueries.Any(query => query.Contains("onlyPending=true")
                && query.Contains("dateFrom=2026-09-01") && !query.Contains("source=")),
                "GOALS canonical exposure query must include every bookmaker within its market.");
    }
}

static void InitialSelectionsAreVisibleAndUnverified()
{
    foreach (var (family, market) in new[]
    {
        ("corners", "HomeTeamCorners"), ("goals", "HomeTeamGoals"),
        ("shots", "TotalShots"), ("sog", "AwayTeamShotsOnGoal")
    })
    {
        var calls = 0;
        var unwantedCalls = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            calls++;
            Equal("/api/automated-corners/selections", request.RequestUri?.AbsolutePath, "initial table endpoint");
            Contains(request.RequestUri?.Query ?? "", $"marketFamily={family.ToUpperInvariant()}");
            var older = request.RequestUri!.Query.Contains("dateTo=2026-08-31");
            var row = new BotPickSelectionViewModel
            {
                AutomatedCornerBetSelectionId = older ? 2 : 1,
                BotKey = older ? "B" : "C2026",
                AutomationVersion = older ? "Bot-B" : "Bot-C2026",
                MarketType = market,
                MatchDate = older ? new DateTime(2026, 8, 20) : new DateTime(2026, 9, 6),
                Status = older ? "Pending" : "Won", Stake = 1m, ProfitLoss = older ? null : 0.8m,
                ProductionPlan = new("stake-1", 1m, "Previous plan", "Previous", "", true)
            };
            return Json(HttpStatusCode.OK, new[] { row });
        })) { BaseAddress = new Uri("http://initial-selections-tests") };
        using var definitionClient = new HttpClient(new StubHandler(_ =>
        {
            unwantedCalls++;
            throw new InvalidOperationException("Initial table must not fetch bot definitions.");
        })) { BaseAddress = new Uri("http://unused-definitions") };
        var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
            new RecommendationAutomationApiClient(definitionClient), NullLogger<BotPicksController>.Instance);
        var result = controller.Selections(new BotPickFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1), DateTo = new DateTime(2026, 9, 6), Bookmaker = "Pinnacle"
        }, family, includePerformance: false).GetAwaiter().GetResult() as JsonResult
            ?? throw new InvalidOperationException($"{family} initial table should load.");
        var rows = result.Value as IReadOnlyList<BotPickSelectionViewModel>
            ?? throw new InvalidOperationException("Selection contract changed.");
        Equal(2, calls, $"{family} initial query count");
        Equal(0, unwantedCalls, $"{family} definition calls");
        Equal(2, rows.Count, $"{family} keeps current and retired older-pending rows");
        Check(rows.All(row => row.ProductionPlan is { Key: "verifying", StakeUnits: 0m, IsProductive: false }),
            $"{family} cannot authorize any stake until full validation.");
        Equal("Won", rows.Single(row => row.AutomatedCornerBetSelectionId == 1).Status, "historical result");
        Equal(0.8m, rows.Single(row => row.AutomatedCornerBetSelectionId == 1).ProfitLoss!.Value, "historical return");
    }
}

static void MonthlyHistoryUsesAggregateApi()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal("/api/automated-corners/monthly-history", request.RequestUri?.AbsolutePath, "aggregate endpoint");
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Contains(query, $"dateFrom={currentMonth.AddMonths(-11):yyyy-MM-dd}");
        Contains(query, $"dateTo={DateTime.Today:yyyy-MM-dd}");
        Contains(query, "marketFamily=GOALS");
        return Json(HttpStatusCode.OK, new[]
        {
            new BotPickMonthlySummaryViewModel
            {
                Month = currentMonth, BotKey = "C", BotLabel = "Bot C", Total = 50,
                Won = 21, Lost = 18, Pending = 9, Push = 1, Void = 1,
                SettledStake = 20m, ProfitLoss = 1.5m, YieldPct = 7.5m
            }
        });
    })) { BaseAddress = new Uri("http://monthly-summary-tests") };
    var controller = new BotPicksController(
        new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(httpClient),
        NullLogger<BotPicksController>.Instance);
    var result = controller.MonthlyHistory("goals").GetAwaiter().GetResult() as JsonResult
        ?? throw new InvalidOperationException("Monthly summary should load.");
    var rows = result.Value as IReadOnlyList<BotPickMonthlySummaryViewModel>
        ?? throw new InvalidOperationException("Monthly summary contract changed.");
    Equal(1, calls, "aggregate requests");
    Equal(50, rows[0].Total, "untruncated total");
    Equal(9, rows[0].Pending, "pending");
    Equal(7.5m, rows[0].YieldPct!.Value, "aggregate yield");
}

static void ClientSendsResearchFilters()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal(HttpMethod.Get, request.Method, "method");
        Equal("/api/automated-corners/research-evaluations", request.RequestUri?.AbsolutePath, "path");
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        Contains(query, "dateFrom=2026-09-01");
        Contains(query, "dateTo=2026-09-04");
        Contains(query, "marketFamily=CORNERS");
        Contains(query, "marketType=AwayTeamCorners");
        Contains(query, "botKey=H");
        Contains(query, "modelDecision=Approved");
        Contains(query, "publicationStatus=ProductionBlocked");
        Contains(query, "page=3");
        Contains(query, "pageSize=25");
        return Json(HttpStatusCode.OK, ResearchPage(page: 3, pageSize: 25));
    }))
    {
        BaseAddress = new Uri("http://research-web-tests")
    };

    var result = new AutomatedCornersApiClient(httpClient).GetResearchEvaluationsAsync(
        new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1),
            DateTo = new DateTime(2026, 9, 4),
            MarketFamily = "CORNERS",
            MarketType = "AwayTeamCorners",
            BotKey = "H",
            ModelDecision = "Approved",
            PublicationStatus = "ProductionBlocked",
            Page = 3,
            PageSize = 25
        },
        CancellationToken.None).GetAwaiter().GetResult();

    Equal(1, calls, "backend calls");
    Equal(4433L, result.TotalCount, "total count");
    Equal(true, result.Items[0].IsResearchWinner, "research winner");
    Equal(2, result.Items[0].ModelDecisionReasons.Count, "reason count");
}

static void ControllerProxiesResearchPage()
{
    var calls = 0;
    using var automatedHttpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        Contains(query, "marketFamily=CORNERS");
        Contains(query, "page=2");
        Contains(query, "pageSize=200");
        return Json(HttpStatusCode.OK, ResearchPage(page: 2, pageSize: 200));
    }))
    {
        BaseAddress = new Uri("http://research-controller-tests")
    };
    using var unusedHttpClient = new HttpClient(new StubHandler(_ =>
        throw new InvalidOperationException("Recommendation API must not be called.")))
    {
        BaseAddress = new Uri("http://unused-recommendations")
    };

    var controller = new BotPicksController(
        new AutomatedCornersApiClient(automatedHttpClient),
        new RecommendationAutomationApiClient(unusedHttpClient),
        NullLogger<BotPicksController>.Instance);

    var action = controller.ResearchEvaluations(
        new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1),
            DateTo = new DateTime(2026, 9, 4),
            MarketFamily = "corners",
            Page = 2,
            PageSize = 999
        },
        CancellationToken.None).GetAwaiter().GetResult();

    var json = action as JsonResult
        ?? throw new InvalidOperationException($"Expected JsonResult, received {action.GetType().Name}.");
    var page = json.Value as BotResearchEvaluationPageViewModel
        ?? throw new InvalidOperationException("Expected research page payload.");
    Equal(1, calls, "backend calls");
    Equal(2, page.Page, "page");

    var invalid = controller.ResearchEvaluations(
        new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 5),
            DateTo = new DateTime(2026, 9, 4)
        },
        CancellationToken.None).GetAwaiter().GetResult();
    Check(invalid is BadRequestObjectResult, "Inverted dates should fail before calling the API.");
    Equal(1, calls, "backend calls after invalid date range");
}

static void ResearchUiIsExplicitlySeparated()
{
    var root = FindRepositoryRoot();
    var index = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Web", "Views", "BotPicks", "Index.cshtml"));
    var partial = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Web", "Views", "BotPicks", "_Research.cshtml"));
    var script = File.ReadAllText(Path.Combine(
        root, "CornersPrediction.Web", "wwwroot", "js", "bot-picks-research.js"));

    Contains(index, "id=\"BotPicksProductionSurface\"");
    Contains(index, "data-bot-picks-surface=\"research\"");
    Contains(partial, "Decisión del modelo");
    Contains(partial, "Estado productivo");
    Contains(partial, "Feature snapshot");
    Contains(partial, "data-endpoint=\"@Url.Action(\"GeneralPicks\"");
    Contains(partial, "data-lab-endpoint=\"@Url.Action(\"GeneralPicksLab\"");
    Contains(partial, "Data Science Lab");
    Contains(script, "modelDecision");
    Contains(script, "publicationStatus");
    Contains(script, "IsResearchWinner");
    Contains(script, "FeatureSnapshotJson");
    Contains(script, "CumulativeProfitLossUnits");
    Contains(script, "BrierScore");
}

static void ApprovedLabUsesCurrentSlice()
{
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal("/api/automated-corners/general-picks/lab", request.RequestUri?.AbsolutePath, "lab endpoint");
        var query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
        Contains(query, "dateFrom=2026-09-01");
        Contains(query, "dateTo=2026-09-06");
        Contains(query, "marketFamily=GOALS");
        Contains(query, "marketType=HomeTeamGoals");
        Contains(query, "botKey=C");
        Contains(query, "publicationStatus=ProductionBlocked");
        Check(!query.Contains("modelDecision=", StringComparison.Ordinal), "The client must not let the table decision alter the approved lab.");
        Check(!query.Contains("page=", StringComparison.Ordinal)
            && !query.Contains("pageSize=", StringComparison.Ordinal)
            && !query.Contains("sortBy=", StringComparison.Ordinal),
            "The aggregate lab must not inherit table pagination or sorting.");
        return Json(HttpStatusCode.OK, new GeneralPickLabViewModel
        {
            Summary = new GeneralPickLabSummaryViewModel
            {
                ApprovedEvaluations = 120,
                IndependentSignals = 30,
                ResolvedSignals = 20,
                ProfitLossUnits = 2.4m,
                Yield = .12m,
                BrierScore = .21
            },
            Timeline = [new(new DateTime(2026, 9, 1), 20, 2.4m, 2.4m)]
        });
    })) { BaseAddress = new Uri("http://general-lab-tests") };
    var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(httpClient), NullLogger<BotPicksController>.Instance);
    var action = controller.GeneralPicksLab(new BotResearchEvaluationFiltersViewModel
    {
        DateFrom = new DateTime(2026, 9, 1),
        DateTo = new DateTime(2026, 9, 6),
        MarketFamily = "goals",
        MarketType = "HomeTeamGoals",
        BotKey = "C",
        ModelDecision = "Rejected",
        PublicationStatus = "ProductionBlocked",
        Page = 7,
        PageSize = 100,
        SortBy = "SelectedOdds"
    }).GetAwaiter().GetResult() as JsonResult
        ?? throw new InvalidOperationException("Expected General Picks lab JSON.");
    var lab = (GeneralPickLabViewModel)action.Value!;
    Equal(1, calls, "lab calls");
    Equal(20, lab.Summary.ResolvedSignals, "resolved lab sample");
    Equal(2.4m, lab.Timeline.Single().CumulativeProfitLossUnits, "lab cumulative result");
}

static void GeneralPicksPreserveAllStatuses()
{
    var rows = new[]
    {
        new BotResearchEvaluationViewModel { EvaluationId = 1, BotKey = "C2026", MarketType = "TotalGoals", ModelDecision = "Approved", PublicationStatus = "ProductionBlocked" },
        new BotResearchEvaluationViewModel { EvaluationId = 2, BotKey = "D2026", MarketType = "HomeTeamGoals", ModelDecision = "Rejected", PublicationStatus = "ModelRejected" },
        new BotResearchEvaluationViewModel { EvaluationId = 3, BotKey = "E2026", MarketType = "AwayTeamGoals", ModelDecision = "Approved", PublicationStatus = "Published", PublishedSelectionId = 101 },
        new BotResearchEvaluationViewModel { EvaluationId = -102, BotKey = "A", RecordKind = "PublishedSelection", MarketType = "TotalGoals", ModelDecision = "NotRecorded", PublicationStatus = "Published", PublishedSelectionId = 102 },
        new BotResearchEvaluationViewModel { EvaluationId = 4, BotKey = "custom-clone", MarketType = "HomeTeamGoals", ModelDecision = "Approved", PublicationStatus = "ProductionBlocked" }
    };
    var calls = 0;
    using var httpClient = new HttpClient(new StubHandler(request =>
    {
        calls++;
        Equal("/api/automated-corners/general-picks", request.RequestUri?.AbsolutePath, "general endpoint without production lookups");
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        Contains(query, "marketFamily=GOALS");
        Check(!query.Contains("modelDecision="), "Default model decision must include all decisions.");
        Check(!query.Contains("botKey="), "Default bot selection must include legacy and custom bots.");
        var blocked = query.Contains("publicationStatus=ProductionBlocked");
        Equal(calls == 2, blocked, "blocked filter is explicit only");
        if (!blocked)
            Check(!query.Contains("publicationStatus="), "Default publication status must include all statuses.");
        var selected = blocked ? rows.Where(row => row.PublicationStatus == "ProductionBlocked").ToArray() : rows;
        return Json(HttpStatusCode.OK, new BotResearchEvaluationPageViewModel
        {
            Items = selected, TotalCount = selected.Length, Page = 1, PageSize = 50, TotalPages = 1,
            AvailableBots = [new("A", "Bot A"), new("custom-clone", "Mi bot")]
        });
    })) { BaseAddress = new Uri("http://general-picks-tests") };
    using var unusedClient = new HttpClient(new StubHandler(_ =>
        throw new InvalidOperationException("General picks must not request production definitions or scorecards.")))
    { BaseAddress = new Uri("http://unused-production") };
    var controller = new BotPicksController(new AutomatedCornersApiClient(httpClient),
        new RecommendationAutomationApiClient(unusedClient), NullLogger<BotPicksController>.Instance);
    foreach (var explicitBlocked in new[] { false, true })
    {
        var response = controller.GeneralPicks(new BotResearchEvaluationFiltersViewModel
        {
            DateFrom = new DateTime(2026, 9, 1), DateTo = new DateTime(2026, 9, 6), MarketFamily = "goals",
            PublicationStatus = explicitBlocked ? "ProductionBlocked" : null
        }).GetAwaiter().GetResult() as JsonResult ?? throw new InvalidOperationException("Expected general picks page.");
        var page = (BotResearchEvaluationPageViewModel)response.Value!;
        Equal(explicitBlocked ? 2 : 5, page.Items.Count, "records preserved by Web proxy");
        Equal("custom-clone", page.AvailableBots[1].BotKey, "custom bot catalog");
        Equal("Mi bot", page.AvailableBots[1].DisplayName, "custom bot label");
        if (!explicitBlocked)
        {
            Equal(3, page.Items.Select(row => row.MarketType).Distinct().Count(), "total/home/away scopes");
            Equal("PublishedSelection", page.Items.Single(row => row.EvaluationId == -102).RecordKind, "legacy provenance");
            Equal("NotRecorded", page.Items.Single(row => row.BotKey == "A").ModelDecision, "legacy decision is not invented");
        }
    }
    Equal(2, calls, "exactly one request per general page");
}

static BotResearchEvaluationPageViewModel ResearchPage(int page, int pageSize) => new()
{
    Items =
    [
        new BotResearchEvaluationViewModel
        {
            EvaluationId = 91,
            RunId = Guid.Parse("f57d9e2b-bd89-4abf-a538-68098d75a86f"),
            BotKey = "H2026",
            MatchDate = new DateTime(2026, 9, 4, 20, 0, 0, DateTimeKind.Utc),
            ModelDecision = "Approved",
            ModelDecisionReasons = ["EDGE_OK", "QUALITY_OK"],
            PublicationStatus = "ProductionBlocked",
            ProductionDecision = "Blocked",
            IsResearchWinner = true,
            EvaluatedAtUtc = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc)
        }
    ],
    TotalCount = 4433,
    Page = page,
    PageSize = pageSize,
    TotalPages = 18
};

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "CornersPrediction.Web")))
            return directory.FullName;
        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Repository root was not found.");
}

static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T value) => new(statusCode)
{
    Content = JsonContent.Create(value)
};

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected '{expected}', received '{actual}'.");
}

static void Contains(string value, string expected)
{
    if (!value.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected '{expected}' in '{value}'.");
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
