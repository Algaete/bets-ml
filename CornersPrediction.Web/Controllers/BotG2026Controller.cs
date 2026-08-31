using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
public sealed class BotG2026Controller : Controller
{
    private static readonly TimeSpan ComponentTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CandidatesTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ScorecardsTimeout = TimeSpan.FromSeconds(60);
    private readonly BotG2026ApiClient _apiClient;
    private readonly ILogger<BotG2026Controller> _logger;

    public BotG2026Controller(
        BotG2026ApiClient apiClient,
        ILogger<BotG2026Controller> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] BotG2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        if (filters.DateToUtc <= filters.DateFromUtc)
            ModelState.AddModelError(string.Empty, "La fecha final debe ser posterior a la inicial.");

        if (!ModelState.IsValid)
            return View(new BotG2026IndexViewModel { Filters = filters });

        try
        {
            var status = await LoadComponentAsync(
                "Estado del runtime",
                _apiClient.GetStatusAsync,
                cancellationToken);

            return View(new BotG2026IndexViewModel
            {
                Filters = filters,
                RuntimeStatus = status.Value ?? new BotG2026RuntimeStatusViewModel(),
                RuntimeStatusErrorMessage = status.ErrorMessage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Candidates(
        [FromQuery] BotG2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        if (filters.DateToUtc <= filters.DateFromUtc)
        {
            return PartialView("_Candidates", new BotG2026IndexViewModel
            {
                Filters = filters,
                CandidatesErrorMessage = "La fecha final debe ser posterior a la inicial."
            });
        }

        try
        {
            var result = await LoadComponentAsync(
                "Candidatos",
                token => _apiClient.GetCandidatesAsync(filters, token),
                cancellationToken,
                CandidatesTimeout);
            return PartialView("_Candidates", new BotG2026IndexViewModel
            {
                Filters = filters,
                Candidates = result.Value ?? new BotG2026CandidatePageViewModel
                {
                    Page = filters.Page,
                    PageSize = filters.PageSize
                },
                CandidatesErrorMessage = result.ErrorMessage
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Scorecards(
        [FromQuery] BotG2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        try
        {
            var result = await LoadComponentAsync(
                "Scorecards",
                token => _apiClient.GetScorecardAsync(filters, token),
                cancellationToken,
                ScorecardsTimeout);
            return PartialView("_Scorecards", new BotG2026IndexViewModel
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

    [HttpGet]
    public async Task<IActionResult> Candidate(
        [FromRoute] long id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            return BadRequest();
        var candidate = await _apiClient.GetCandidateAsync(id, cancellationToken);
        return candidate is null ? NotFound() : Json(candidate);
    }

    private static void ApplyDefaults(BotG2026FiltersViewModel filters)
    {
        var utcToday = DateTime.UtcNow.Date;
        filters.DateFromUtc ??= utcToday.AddDays(-30);
        filters.DateToUtc ??= utcToday.AddDays(8);
        filters.Page = Math.Max(1, filters.Page);
        filters.PageSize = Math.Clamp(filters.PageSize, 1, 250);
    }

    private async Task<ComponentLoadResult<T>> LoadComponentAsync<T>(
        string component,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellationToken,
        TimeSpan? componentTimeout = null)
    {
        var effectiveTimeout = componentTimeout ?? ComponentTimeout;
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
                "Bot G2026 {Component} timed out after {TimeoutSeconds} seconds",
                component,
                effectiveTimeout.TotalSeconds);
            return ComponentLoadResult<T>.Failure(
                $"El componente «{component}» no respondió: la API demoró más de {effectiveTimeout.TotalSeconds:0} segundos. " +
                "La tabla principal sigue disponible; puedes reintentar sólo este bloque.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not load Bot G2026 {Component}", component);
            return ComponentLoadResult<T>.Failure(BuildHttpFailureMessage(component, exception.StatusCode));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot G2026 {Component}", component);
            return ComponentLoadResult<T>.Failure(
                $"No se pudo cargar el componente «{component}». Revisa la API y la conexión SQL de este componente.");
        }
    }

    private static string BuildHttpFailureMessage(string component, HttpStatusCode? statusCode)
    {
        var status = statusCode.HasValue ? $" (API HTTP {(int)statusCode.Value})" : string.Empty;
        return $"No se pudo cargar el componente «{component}»{status}. " +
               "Revisa la API, la migración y la conexión SQL de este componente; el resto del laboratorio continúa disponible.";
    }

    private sealed record ComponentLoadResult<T>(T? Value, string? ErrorMessage)
    {
        public static ComponentLoadResult<T> Success(T value) => new(value, null);
        public static ComponentLoadResult<T> Failure(string message) => new(default, message);
    }
}
