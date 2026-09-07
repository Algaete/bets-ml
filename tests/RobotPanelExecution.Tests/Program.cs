using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CornersPrediction.Application.Automation;
using CornersPrediction.Infrastructure.Automation;
using CornersPrediction.Infrastructure.Options;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Controllers;
using CornersPrediction.Web.Models.RobotPanel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var jobs = new Jobs();
var definitions = new Definitions();
var handler = new Handler();
using var client = new HttpClient(handler) { BaseAddress = new Uri("http://panel-test") };
var service = new CornersPipelineService(new Factory(client), Options.Create(new CornersAutomationOptions()),
    NullLogger<CornersPipelineService>.Instance, jobs, definitions);
var queued = await service.RunBotsAsync(new RunBotsCommand(BatchNumber: 2, BatchSize: 100, UpcomingDays: 14), CancellationToken.None);
Check(queued.Status == "Queued" && !queued.IsSuccess && queued.RecommendationJob is not null, "Queue acceptance must never report completed success.");
Check(handler.Calls == 0, "Enabled bots must queue directly without invoking the long HTTP bot run.");
Check(jobs.LastCommand is { Mode: "Live", BatchSize: 10 }, "Queue must process all markets in live batches of 10.");
Check(jobs.LastCommand!.DateTo.DayNumber - jobs.LastCommand.DateFrom.DayNumber == 14, "Requested upcoming date range must be preserved.");
Check(jobs.LastCommand.BotKeys!.SequenceEqual(new[] { "A", "C2026", "CUSTOM" }), "Bot keys must come from enabled server definitions, including paused publication bots and custom bots.");
Console.WriteLine("PASS queued all enabled bots without HTTP run; preserved range, publication pause, custom bots and batches of 10");

definitions.Values = [];
var noBots = await service.RunBotsAsync(new RunBotsCommand(), CancellationToken.None);
Check(noBots.Status == "Skipped" && !noBots.IsSuccess && jobs.Calls == 1, "Empty enabled set must not fall back to C2026.");
definitions.Values = Definitions.Defaults();

handler.Respond = _ => Json(new { totalMatches = 10, errorMatches = 2, skipped = Array.Empty<object>(), selections = Array.Empty<object>() });
var partial = await service.RunBotsAsync(new RunBotsCommand(RunAllEnabledBots: false), CancellationToken.None);
Check(partial.Status == "PartialSuccess" && partial.Errors == 2, "Per-match errors must be visible as partial success.");
handler.Respond = _ => Json(new { totalMatches = 10, errorMatches = 10, skipped = Array.Empty<object>(), selections = Array.Empty<object>() });
var failed = await service.RunBotsAsync(new RunBotsCommand(RunAllEnabledBots: false), CancellationToken.None);
Check(failed.Status == "Failed" && !failed.IsSuccess, "An all-failed run must stay failed.");
Console.WriteLine("PASS missing enabled bots, partial failures and all-match failures report accurately");

handler.Respond = request => request.RequestUri!.AbsolutePath switch
{
    "/api/api-football/bulk-sync" => Json(new { errors = 0 }),
    "/api/api-football/sync-upcoming" => Json(new { }),
    "/api/PinnacleOddsScrapping/scrape-upcoming-football" => Json(new { }),
    _ => throw new Exception("Full pipeline unexpectedly called: " + request.RequestUri)
};
var full = await service.RunFullPipelineAsync(new RunFullPipelineCommand(), CancellationToken.None);
// Betano is disabled in this fixture, so the full pipeline legitimately remains partial.
Check(full.Steps.Last().Status == "Queued" && full.Steps.Last().RecommendationJob is not null,
    "Full pipeline must queue its bot stage instead of awaiting the evaluation.");
Check(full.Status != "Success", "Queued work is not a completed full pipeline.");
Console.WriteLine("PASS full pipeline queues its final bot step without premature success");

handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
var controller = new RobotPanelController(new CornersPipelineApiClient(client), NullLogger<RobotPanelController>.Instance);
var before = handler.Calls;
Check(await controller.BotJobStatus(Guid.Empty, CancellationToken.None) is BadRequestObjectResult, "Empty job id must return 400.");
Check(handler.Calls == before, "Empty job id must not call backend.");
Check(await controller.BotJobStatus(Guid.NewGuid(), CancellationToken.None) is NotFoundObjectResult, "Missing job must return 404 so stale local tracking can clear.");
handler.Respond = _ => Json(queued);
var response = await controller.RunBots(new RobotPanelBotsRequestViewModel { UpcomingDays = 14 }, CancellationToken.None);
Check(response is JsonResult { Value: RobotPanelStepResultViewModel { Status: "Queued", RecommendationJob: not null } },
    "MVC must preserve queued state and job identity for polling.");
Console.WriteLine("PASS MVC job tracking validation and queued response identity");

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
sealed class Handler : HttpMessageHandler
{
    public int Calls;
    public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => throw new Exception("Unexpected HTTP request");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { Calls++; return Task.FromResult(Respond(request)); }
}
sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
sealed class Jobs : IRecommendationJobsUseCase
{
    public int Calls;
    public CreateRecommendationJobCommand? LastCommand;
    public Task<RecommendationJobDto> EnqueueAsync(CreateRecommendationJobCommand command, CancellationToken cancellationToken)
    {
        Calls++; LastCommand = command;
        return Task.FromResult(new RecommendationJobDto(Guid.NewGuid(), command.Name!, "Queued", command.Mode,
            command.DateFrom, command.DateTo, command.BotKeys!.ToArray(), ["CORNERS", "GOALS", "SHOTS", "SOG"], command.BatchSize,
            1, null, 0, 0, 0, 0, 0, 0, 0, 3, null, null, DateTime.UtcNow, null, DateTime.UtcNow, null));
    }
    public Task<RecommendationJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<RecommendationJobDto>> ListAsync(int take, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
sealed class Definitions : IRecommendationBotDefinitionsUseCase
{
    public IReadOnlyList<RecommendationBotDefinitionDto> Values = Defaults();
    public static IReadOnlyList<RecommendationBotDefinitionDto> Defaults() =>
        [Bot("A"), Bot("C2026") with { PublishEnabled = false }, Bot("CUSTOM"), Bot("DISABLED") with { IsEnabled = false }, Bot("I2026") with { SupportsRecommendationJobs = false }];
    static RecommendationBotDefinitionDto Bot(string key) => new(key, key, "", "MODELS_2026", true, true, true,
        ["CORNERS", "GOALS", "SHOTS", "SOG"], null, null, null, null, null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
    public Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Values);
    public Task<RecommendationBotDefinitionDto?> GetAsync(string botKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<RecommendationBotLeagueCatalogItem>> GetLeagueCatalogAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<RecommendationBotDefinitionDto> SaveAsync(SaveRecommendationBotDefinitionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken) => throw new NotSupportedException();
}
