using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Controllers;
using CornersPrediction.Web.Models.BotPicks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

var candidateAction = typeof(BotG2026Controller).GetMethod(nameof(BotG2026Controller.Candidate))
    ?? throw new InvalidOperationException("Bot G candidate action was not found.");
var candidateIdParameter = candidateAction.GetParameters()
    .Single(parameter => parameter.Name == "id");
Check(candidateIdParameter.GetCustomAttribute<FromRouteAttribute>() is not null,
    "Bot G candidate id must bind from the conventional /BotG2026/Candidate/{id} route.");
Console.WriteLine("PASS candidate id binds from route");

var candidateBackendCalls = 0;
using (var candidateHttpClient = new HttpClient(new StubHandler(request =>
       {
           candidateBackendCalls++;
           Check(request.RequestUri?.AbsolutePath == "/api/bot-g2026/candidates/137465",
               $"Unexpected candidate API path: {request.RequestUri?.AbsolutePath}");
           return Json(HttpStatusCode.OK, new BotG2026CandidateViewModel { CandidateId = 137465 });
       }))
       { BaseAddress = new Uri("http://bot-g-web-tests") })
{
    var candidateController = new BotG2026Controller(
        new BotG2026ApiClient(candidateHttpClient),
        NullLogger<BotG2026Controller>.Instance);
    var candidateResult = candidateController.Candidate(137465, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    Check(candidateResult is JsonResult
        {
            Value: BotG2026CandidateViewModel { CandidateId: 137465 }
        }, "Candidate detail should return the requested candidate as JSON.");
    Check(candidateBackendCalls == 1, "Candidate detail should call the API exactly once.");

    var invalidCandidateResult = candidateController.Candidate(0, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    Check(invalidCandidateResult is BadRequestResult,
        "Candidate id zero should return HTTP 400.");
    Check(candidateBackendCalls == 1,
        "Candidate id zero must not call the API.");
}
Console.WriteLine("PASS candidate detail route and API lookup");

var scenarios = new (string Name, Func<HttpRequestMessage, HttpResponseMessage> Respond, Action<BotG2026IndexViewModel> Assert)[]
{
    (
        "candidate failure preserves runtime and scorecards",
        request => ResponseFor(request, failComponent: "candidates"),
        model =>
        {
            Check(model.RuntimeStatusAvailable && model.RuntimeStatus.Available, "Runtime should remain visible.");
            Check(model.ScorecardsAvailable && model.Scorecards.Count == 1, "Scorecards should remain visible.");
            Check(!model.CandidatesAvailable, "Candidates should report their own failure.");
            CheckContains(model.CandidatesErrorMessage, "HTTP 500");
        }),
    (
        "scorecard failure preserves runtime and candidates",
        request => ResponseFor(request, failComponent: "scorecards"),
        model =>
        {
            Check(model.RuntimeStatusAvailable && model.RuntimeStatus.Available, "Runtime should remain visible.");
            Check(model.CandidatesAvailable && model.Candidates.TotalRows == 1, "Candidates should remain visible.");
            Check(!model.ScorecardsAvailable, "Scorecards should report their own failure.");
            CheckContains(model.ScorecardsErrorMessage, "HTTP 500");
        }),
    (
        "runtime failure preserves both data components",
        request => ResponseFor(request, failComponent: "runtime"),
        model =>
        {
            Check(!model.RuntimeStatusAvailable, "Runtime should report its own failure.");
            Check(model.CandidatesAvailable && model.Candidates.TotalRows == 1, "Candidates should remain visible.");
            Check(model.ScorecardsAvailable && model.Scorecards.Count == 1, "Scorecards should remain visible.");
        })
};

foreach (var scenario in scenarios)
{
    using var httpClient = new HttpClient(new StubHandler(scenario.Respond))
    {
        BaseAddress = new Uri("http://bot-g-web-tests")
    };
    var controller = new BotG2026Controller(
        new BotG2026ApiClient(httpClient),
        NullLogger<BotG2026Controller>.Instance);

    var filters = new BotG2026FiltersViewModel();
    var indexAction = controller.Index(filters, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    var indexModel = ModelFrom(indexAction);
    var candidatesAction = controller.Candidates(filters, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    var candidatesModel = ModelFrom(candidatesAction);
    var scorecardsAction = controller.Scorecards(filters, CancellationToken.None)
        .GetAwaiter()
        .GetResult();
    var scorecardsModel = ModelFrom(scorecardsAction);
    var model = new BotG2026IndexViewModel
    {
        Filters = filters,
        RuntimeStatus = indexModel.RuntimeStatus,
        RuntimeStatusErrorMessage = indexModel.RuntimeStatusErrorMessage,
        Candidates = candidatesModel.Candidates,
        CandidatesErrorMessage = candidatesModel.CandidatesErrorMessage,
        Scorecards = scorecardsModel.Scorecards,
        ScorecardsErrorMessage = scorecardsModel.ScorecardsErrorMessage
    };

    scenario.Assert(model);
    Console.WriteLine($"PASS {scenario.Name}");
}

Console.WriteLine($"All {scenarios.Length + 2} Bot G Web tests passed.");

static HttpResponseMessage ResponseFor(HttpRequestMessage request, string failComponent)
{
    var path = request.RequestUri?.AbsolutePath ?? string.Empty;
    var component = path.EndsWith("/status", StringComparison.Ordinal)
        ? "runtime"
        : path.EndsWith("/scorecard", StringComparison.Ordinal)
            ? "scorecards"
            : "candidates";
    if (component == failComponent)
    {
        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($"{component} query failed")
        };
    }

    if (component == "runtime")
    {
        return Json(HttpStatusCode.OK, new BotG2026RuntimeStatusViewModel
        {
            Enabled = true,
            Available = true,
            State = "Ready",
            Message = "Runtime de prueba disponible."
        });
    }

    if (component == "scorecards")
    {
        IReadOnlyList<BotG2026ScorecardViewModel> rows =
        [
            new BotG2026ScorecardViewModel
            {
                Dimension = "Overall",
                Segment = "All",
                CandidatesEvaluated = 1
            }
        ];
        return Json(HttpStatusCode.OK, rows);
    }

    return Json(HttpStatusCode.OK, new BotG2026CandidatePageViewModel
    {
        Items = [new BotG2026CandidateViewModel { CandidateId = 1 }],
        TotalRows = 1,
        Page = 1,
        PageSize = 100
    });
}

static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T value) => new(statusCode)
{
    Content = JsonContent.Create(value)
};

static BotG2026IndexViewModel ModelFrom(IActionResult action)
{
    var value = action switch
    {
        ViewResult view => view.Model,
        PartialViewResult partial => partial.Model,
        _ => throw new InvalidOperationException($"Expected a view result, got {action.GetType().Name}.")
    };
    return value as BotG2026IndexViewModel
        ?? throw new InvalidOperationException("Expected BotG2026IndexViewModel.");
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void CheckContains(string? value, string expected)
{
    if (value?.Contains(expected, StringComparison.OrdinalIgnoreCase) != true)
        throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
