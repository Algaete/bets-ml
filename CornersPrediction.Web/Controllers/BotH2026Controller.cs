using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
public sealed class BotH2026Controller : Controller
{
    private static readonly TimeSpan ComponentTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan EvaluationsTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ScorecardsTimeout = TimeSpan.FromSeconds(45);
    private readonly BotH2026ApiClient _apiClient;
    private readonly ILogger<BotH2026Controller> _logger;

    public BotH2026Controller(
        BotH2026ApiClient apiClient,
        ILogger<BotH2026Controller> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index([FromQuery] BotH2026FiltersViewModel filters)
    {
        ApplyDefaults(filters);
        if (filters.PredictionToUtc <= filters.PredictionFromUtc)
            ModelState.AddModelError(string.Empty, "La fecha final debe ser posterior a la inicial.");

        if (!ModelState.IsValid)
            return View(new BotH2026IndexViewModel { Filters = filters });

        return View(new BotH2026IndexViewModel { Filters = filters });
    }

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await LoadComponentAsync(
                "Estado",
                _apiClient.GetStatusAsync,
                cancellationToken,
                StatusTimeout);
            return PartialView("_Status", new BotH2026IndexViewModel
            {
                Status = result.Value ?? new BotH2026StatusViewModel(),
                StatusErrorMessage = result.ErrorMessage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Evaluations(
        [FromQuery] BotH2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        try
        {
            var result = await LoadComponentAsync(
                "Evaluaciones",
                token => _apiClient.GetEvaluationsAsync(filters, token),
                cancellationToken,
                EvaluationsTimeout);
            return PartialView("_Evaluations", new BotH2026IndexViewModel
            {
                Filters = filters,
                Evaluations = result.Value ?? new BotH2026EvaluationPageViewModel
                {
                    Page = filters.Page,
                    PageSize = filters.PageSize
                },
                EvaluationsErrorMessage = result.ErrorMessage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Scorecards(
        [FromQuery] BotH2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        try
        {
            var result = await LoadComponentAsync(
                "Scorecards",
                token => _apiClient.GetScorecardsAsync(filters, token),
                cancellationToken,
                ScorecardsTimeout);
            return PartialView("_Scorecards", new BotH2026IndexViewModel
            {
                Filters = filters,
                Scorecards = result.Value ?? [],
                ScorecardsErrorMessage = result.ErrorMessage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    private static void ApplyDefaults(BotH2026FiltersViewModel filters)
    {
        var utcToday = DateTime.UtcNow.Date;
        filters.PredictionFromUtc ??= utcToday.AddDays(-30);
        filters.PredictionToUtc ??= utcToday.AddDays(1);
        filters.Page = Math.Max(1, filters.Page);
        filters.PageSize = Math.Clamp(filters.PageSize, 1, 250);
    }

    private async Task<ComponentLoadResult<T>> LoadComponentAsync<T>(
        string component,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellationToken,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? ComponentTimeout;
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutCancellation.CancelAfter(effectiveTimeout);
        try
        {
            return ComponentLoadResult<T>.Success(await operation(timeoutCancellation.Token));
        }
        catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(exception,
                "Bot H2026 {Component} timed out after {TimeoutSeconds} seconds",
                component,
                effectiveTimeout.TotalSeconds);
            return ComponentLoadResult<T>.Failure(
                $"El bloque «{component}» demoró más de {effectiveTimeout.TotalSeconds:0} segundos. " +
                "H2026 continúa disponible y puedes reintentar sólo este bloque.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not load Bot H2026 {Component}", component);
            var status = exception.StatusCode.HasValue ? $" (API HTTP {(int)exception.StatusCode.Value})" : string.Empty;
            return ComponentLoadResult<T>.Failure(
                $"No se pudo cargar «{component}»{status}; los demás bloques de H2026 continúan disponibles.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot H2026 {Component}", component);
            return ComponentLoadResult<T>.Failure(
                $"No se pudo cargar «{component}». Revisa la API y la conexión SQL de este bloque.");
        }
    }

    private sealed record ComponentLoadResult<T>(T? Value, string? ErrorMessage)
    {
        public static ComponentLoadResult<T> Success(T value) => new(value, null);
        public static ComponentLoadResult<T> Failure(string message) => new(default, message);
    }
}
