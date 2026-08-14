using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.RobotPanel;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Admin)]
public sealed class RobotPanelController : Controller
{
    private readonly CornersPipelineApiClient _cornersPipelineApiClient;
    private readonly ILogger<RobotPanelController> _logger;

    public RobotPanelController(
        CornersPipelineApiClient cornersPipelineApiClient,
        ILogger<RobotPanelController> logger)
    {
        _cornersPipelineApiClient = cornersPipelineApiClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index([FromQuery] int? days = null)
    {
        return View(new RobotPanelIndexViewModel
        {
            SelectedDays = NormalizeDays(days),
            UpcomingDays = 7
        });
    }

    [HttpPost]
    public Task<IActionResult> RunMatchHistory(
        [FromBody] RobotPanelStepRequestViewModel request,
        CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            () => _cornersPipelineApiClient.RunMatchHistoryAsync(NormalizeDays(request.Days), cancellationToken),
            "match history",
            cancellationToken);

    [HttpPost]
    public Task<IActionResult> RunUpcomingMatches(
        [FromBody] RobotPanelStepRequestViewModel request,
        CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            () => _cornersPipelineApiClient.RunUpcomingMatchesAsync(NormalizeDays(request.Days), cancellationToken),
            "upcoming matches",
            cancellationToken);

    [HttpPost]
    public Task<IActionResult> RunPinnacleOdds(CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            () => _cornersPipelineApiClient.RunPinnacleOddsAsync(cancellationToken),
            "pinnacle odds",
            cancellationToken);

    [HttpPost]
    public Task<IActionResult> RunBetanoOdds(CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            () => _cornersPipelineApiClient.RunBetanoOddsAsync(cancellationToken),
            "betano odds",
            cancellationToken);

    [HttpGet]
    public async Task<IActionResult> ApiFootballHistoricalStatus(CancellationToken cancellationToken)
    {
        try
        {
            return Json(await _cornersPipelineApiClient.GetApiFootballHistoricalBatchAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load the API-Football historical batch state");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = BuildExecutionErrorMessage("API-Football historical status", exception) });
        }
    }

    [HttpPost]
    public async Task<IActionResult> StartApiFootballHistorical(CancellationToken cancellationToken)
    {
        try
        {
            return Json(await _cornersPipelineApiClient.StartApiFootballHistoricalBatchAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start the API-Football historical batch");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = BuildExecutionErrorMessage("API-Football historical batch", exception) });
        }
    }

    [HttpPost]
    public Task<IActionResult> RunBots(
        [FromBody] RobotPanelBotsRequestViewModel? request,
        CancellationToken cancellationToken) =>
        ExecuteStepAsync(
            () => _cornersPipelineApiClient.RunBotsAsync(
                request?.ExcludeExistingSelections ?? false,
                Math.Max(1, request?.BatchNumber ?? 1),
                NormalizeBatchSize(request?.BatchSize ?? 100),
                request?.RunBotC ?? true,
                cancellationToken),
            "bot execution",
            cancellationToken);

    [HttpGet]
    public async Task<IActionResult> BotAvailability(CancellationToken cancellationToken)
    {
        try
        {
            return Json(await _cornersPipelineApiClient.GetBotAvailabilityAsync(100, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load bot odds availability from the MVC app");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = BuildExecutionErrorMessage("bot availability request", exception) });
        }
    }

    [HttpPost]
    public async Task<IActionResult> RunFullPipeline(
        [FromBody] RobotPanelFullRunRequestViewModel request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _cornersPipelineApiClient.RunFullPipelineAsync(
                matchHistoryDays: NormalizeDays(request.MatchHistoryDays),
                upcomingDays: NormalizeDays(request.UpcomingDays),
                excludeExistingSelections: request.ExcludeExistingSelections,
                botBatchNumber: Math.Max(1, request.BotBatchNumber),
                botBatchSize: NormalizeBatchSize(request.BotBatchSize),
                runBotC: request.RunBotC,
                cancellationToken);

            return Json(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "Full pipeline request was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not execute the robot pipeline from the MVC app");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = BuildExecutionErrorMessage("robot pipeline", exception) });
        }
    }

    private async Task<IActionResult> ExecuteStepAsync(
        Func<Task<RobotPanelStepResultViewModel>> action,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return Json(await action());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = $"The {operationName} request was cancelled." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not execute {OperationName} from the MVC app", operationName);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = BuildExecutionErrorMessage($"{operationName} request", exception) });
        }
    }

    private static string BuildExecutionErrorMessage(string operationName, Exception exception)
    {
        var backendError = TryExtractBackendError(exception.Message);
        if (!string.IsNullOrWhiteSpace(backendError))
        {
            return $"The {operationName} could not be executed. Backend detail: {backendError}";
        }

        return $"The {operationName} could not be executed. Backend detail: {exception.Message}";
    }

    private static string? TryExtractBackendError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "error", "detail", "title", "message" })
            {
                if (root.TryGetProperty(propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return message;
        }

        return message;
    }

    private static int NormalizeDays(int? days)
    {
        if (days is null or <= 0)
        {
            return 7;
        }

        return Math.Min(days.Value, 30);
    }

    private static int NormalizeBatchSize(int batchSize) =>
        Math.Clamp(batchSize <= 0 ? 100 : batchSize, 1, 100);
}
