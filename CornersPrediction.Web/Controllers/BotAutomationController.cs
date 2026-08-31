using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotAutomation;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Admin)]
public sealed class BotAutomationController : Controller
{
    private static readonly TimeSpan ComponentTimeout = TimeSpan.FromSeconds(15);
    private readonly RecommendationAutomationApiClient _apiClient;
    private readonly ILogger<BotAutomationController> _logger;

    public BotAutomationController(
        RecommendationAutomationApiClient apiClient,
        ILogger<BotAutomationController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        try
        {
            var botsTask = LoadComponentAsync(
                "bots",
                token => _apiClient.GetBotsAsync(token),
                cancellationToken);
            var jobsTask = LoadComponentAsync(
                "procesos",
                token => _apiClient.GetJobsAsync(50, token),
                cancellationToken);
            var leagueCatalogTask = LoadComponentAsync(
                "catálogo de ligas",
                token => _apiClient.GetLeagueCatalogAsync(token),
                cancellationToken);
            await Task.WhenAll(botsTask, jobsTask, leagueCatalogTask);

            var bots = await botsTask;
            var jobs = await jobsTask;
            var leagueCatalog = await leagueCatalogTask;
            return View(new BotAutomationIndexViewModel
            {
                Bots = bots.Value ?? [],
                Jobs = jobs.Value ?? [],
                LeagueCatalog = leagueCatalog.Value ?? [],
                DefaultDateFrom = today,
                DefaultDateTo = today.AddDays(7),
                BotsLoadError = bots.ErrorMessage,
                JobsLoadError = jobs.ErrorMessage,
                LeagueCatalogLoadError = leagueCatalog.ErrorMessage
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load bot automation administration");
            return View(new BotAutomationIndexViewModel
            {
                DefaultDateFrom = today,
                DefaultDateTo = today.AddDays(7),
                LoadError = exception.Message
            });
        }
    }

    private async Task<ComponentLoadResult<T>> LoadComponentAsync<T>(
        string component,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutCancellation.CancelAfter(ComponentTimeout);

        try
        {
            return ComponentLoadResult<T>.Success(await operation(timeoutCancellation.Token));
        }
        catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load bot automation {Component}", component);
            var detail = exception is OperationCanceledException
                ? $"La consulta superó {ComponentTimeout.TotalSeconds:0} segundos."
                : exception.Message;
            return ComponentLoadResult<T>.Failure($"{component}: {detail}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Bots(CancellationToken cancellationToken) =>
        await ProxyAsync(() => _apiClient.GetBotsAsync(cancellationToken), "load bots");

    [HttpGet]
    public async Task<IActionResult> Jobs(CancellationToken cancellationToken) =>
        await ProxyAsync(() => _apiClient.GetJobsAsync(50, cancellationToken), "load recommendation jobs");

    [HttpPost]
    public async Task<IActionResult> EnqueueJob(
        [FromBody] CreateRecommendationJobViewModel request,
        CancellationToken cancellationToken) =>
        await ProxyAsync(() => _apiClient.EnqueueJobAsync(request, cancellationToken), "enqueue recommendation job");

    [HttpPost]
    public async Task<IActionResult> SaveBot(
        [FromBody] SaveRecommendationBotDefinitionViewModel request,
        CancellationToken cancellationToken) =>
        await ProxyAsync(() => _apiClient.SaveBotAsync(request, cancellationToken), "save bot definition");

    [HttpDelete]
    public async Task<IActionResult> CancelJob(Guid id, CancellationToken cancellationToken) =>
        await ProxyNoContentAsync(() => _apiClient.CancelJobAsync(id, cancellationToken), "cancel recommendation job");

    [HttpDelete]
    public async Task<IActionResult> DeleteBot(string botKey, CancellationToken cancellationToken) =>
        await ProxyNoContentAsync(() => _apiClient.DeleteBotAsync(botKey, cancellationToken), "delete bot definition");

    private async Task<IActionResult> ProxyAsync<T>(Func<Task<T>> operation, string operationName)
    {
        try
        {
            return Json(await operation());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not {OperationName} from the MVC app", operationName);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    private async Task<IActionResult> ProxyNoContentAsync(Func<Task> operation, string operationName)
    {
        try
        {
            await operation();
            return NoContent();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not {OperationName} from the MVC app", operationName);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    private sealed record ComponentLoadResult<T>(T? Value, string? ErrorMessage)
    {
        public static ComponentLoadResult<T> Success(T value) => new(value, null);
        public static ComponentLoadResult<T> Failure(string message) => new(default, message);
    }
}
