using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotAutomation;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Admin)]
public sealed class BotAutomationController : Controller
{
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
            var botsTask = _apiClient.GetBotsAsync(cancellationToken);
            var jobsTask = _apiClient.GetJobsAsync(50, cancellationToken);
            await Task.WhenAll(botsTask, jobsTask);
            return View(new BotAutomationIndexViewModel
            {
                Bots = await botsTask,
                Jobs = await jobsTask,
                DefaultDateFrom = today,
                DefaultDateTo = today.AddDays(7)
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
}
