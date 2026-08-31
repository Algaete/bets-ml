using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.BotPicks;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize(Policy = PlatformPolicies.Predictions)]
public sealed class BotI2026Controller : Controller
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EvaluationsTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ScorecardsTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromMinutes(2);
    private readonly BotI2026ApiClient _apiClient;
    private readonly ILogger<BotI2026Controller> _logger;

    public BotI2026Controller(
        BotI2026ApiClient apiClient,
        ILogger<BotI2026Controller> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index([FromQuery] BotI2026FiltersViewModel filters)
    {
        ApplyDefaults(filters);
        if (filters.PredictionToUtc <= filters.PredictionFromUtc)
            ModelState.AddModelError(string.Empty, "La fecha final debe ser posterior a la inicial.");

        var today = SantiagoToday();
        return View(new BotI2026IndexViewModel
        {
            Filters = filters,
            Collection = new BotI2026CollectViewModel
            {
                DateFrom = today,
                DateTo = today.AddDays(8),
                MaximumFixtures = 50
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await LoadComponentAsync(
                "Estado",
                _apiClient.GetStatusAsync,
                StatusTimeout,
                cancellationToken);
            return PartialView("_Status", new BotI2026IndexViewModel
            {
                Status = result.Value ?? new BotI2026StatusViewModel(),
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
        [FromQuery] BotI2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        try
        {
            var result = await LoadComponentAsync(
                "Evaluaciones",
                token => _apiClient.GetEvaluationsAsync(filters, token),
                EvaluationsTimeout,
                cancellationToken);
            return PartialView("_Evaluations", new BotI2026IndexViewModel
            {
                Filters = filters,
                Evaluations = result.Value ?? new BotI2026EvaluationPageViewModel
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
        [FromQuery] BotI2026FiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        ApplyDefaults(filters);
        try
        {
            var result = await LoadComponentAsync(
                "Scorecards",
                token => _apiClient.GetScorecardsAsync(filters, token),
                ScorecardsTimeout,
                cancellationToken);
            return PartialView("_Scorecards", new BotI2026IndexViewModel
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Collect(
        [FromForm] BotI2026CollectViewModel command,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCollection(command);
        if (validationError is not null)
            return BadRequest(new { ok = false, message = validationError });

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(CollectionTimeout);
        try
        {
            var result = await _apiClient.CollectAsync(command, timeoutCancellation.Token);
            return Json(new
            {
                ok = true,
                message = $"I2026 guardó {result.Inserted:N0} evaluaciones shadow; " +
                          $"{result.AlreadyCaptured:N0} ya existían. No se publicó ninguna apuesta.",
                result
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(exception, "Bot I2026 shadow collection timed out");
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                ok = false,
                message = "La recolección demoró más de 2 minutos. No se creó ninguna apuesta productiva; puedes reintentar sin duplicar evidencia."
            });
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Bot I2026 shadow collection failed");
            var status = exception.StatusCode.HasValue ? $" HTTP {(int)exception.StatusCode.Value}" : string.Empty;
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                message = $"La API no pudo recolectar evidencia shadow ({status.Trim()}). No se publicó ninguna apuesta."
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Bot I2026 shadow collection failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                message = "No se pudo recolectar la evidencia shadow. No se publicó ninguna apuesta."
            });
        }
    }

    private static void ApplyDefaults(BotI2026FiltersViewModel filters)
    {
        var utcToday = DateTime.UtcNow.Date;
        filters.PredictionFromUtc ??= utcToday.AddDays(-30);
        filters.PredictionToUtc ??= utcToday.AddDays(1);
        filters.Page = Math.Max(1, filters.Page);
        filters.PageSize = Math.Clamp(filters.PageSize, 1, 250);
    }

    private static string? ValidateCollection(BotI2026CollectViewModel command)
    {
        if (command.DateFrom == default || command.DateTo == default)
            return "Debes indicar las fechas de recolección.";
        if (command.DateTo <= command.DateFrom)
            return "La fecha final debe ser posterior a la inicial.";
        if (command.DateTo.DayNumber - command.DateFrom.DayNumber > 14)
            return "La recolección puede abarcar como máximo 14 días.";
        if (command.MaximumFixtures is < 1 or > 1000)
            return "El máximo de partidos debe estar entre 1 y 1.000.";
        return null;
    }

    private async Task<ComponentLoadResult<T>> LoadComponentAsync<T>(
        string component,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken requestCancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutCancellation.CancelAfter(timeout);
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
                "Bot I2026 {Component} timed out after {TimeoutSeconds} seconds",
                component,
                timeout.TotalSeconds);
            return ComponentLoadResult<T>.Failure(
                $"El bloque «{component}» demoró más de {timeout.TotalSeconds:0} segundos. " +
                "Los demás bloques de I2026 siguen disponibles.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not load Bot I2026 {Component}", component);
            var status = exception.StatusCode.HasValue ? $" (API HTTP {(int)exception.StatusCode.Value})" : string.Empty;
            return ComponentLoadResult<T>.Failure(
                $"No se pudo cargar «{component}»{status}; los demás bloques siguen disponibles.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load Bot I2026 {Component}", component);
            return ComponentLoadResult<T>.Failure(
                $"No se pudo cargar «{component}». Revisa la API y la conexión SQL de este bloque.");
        }
    }

    private static DateOnly SantiagoToday()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(id)));
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed record ComponentLoadResult<T>(T? Value, string? ErrorMessage)
    {
        public static ComponentLoadResult<T> Success(T value) => new(value, null);
        public static ComponentLoadResult<T> Failure(string message) => new(default, message);
    }
}
