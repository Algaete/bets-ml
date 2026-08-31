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
    private static readonly TimeSpan ThresholdAnalysisTimeout = TimeSpan.FromSeconds(60);
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

        var thresholdFilters = CreateThresholdDefaults(filters);

        if (!ModelState.IsValid)
            return View(new BotH2026IndexViewModel { Filters = filters, ThresholdFilters = thresholdFilters });

        return View(new BotH2026IndexViewModel { Filters = filters, ThresholdFilters = thresholdFilters });
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

    [HttpGet]
    public async Task<IActionResult> ThresholdAnalysis(
        [FromQuery] BotH2026ThresholdAnalysisFiltersViewModel filters,
        CancellationToken cancellationToken = default)
    {
        NormalizeThresholdFilters(filters);
        var validationError = ValidateThresholdFilters(filters);
        if (validationError is not null)
        {
            return PartialView("_ThresholdAnalysis", new BotH2026IndexViewModel
            {
                ThresholdFilters = filters,
                ThresholdAnalysisRequested = true,
                ThresholdAnalysisErrorMessage = validationError
            });
        }

        try
        {
            var result = await LoadComponentAsync(
                "Análisis de umbrales",
                token => _apiClient.GetThresholdAnalysisAsync(filters, token),
                cancellationToken,
                ThresholdAnalysisTimeout);
            return PartialView("_ThresholdAnalysis", new BotH2026IndexViewModel
            {
                ThresholdFilters = filters,
                ThresholdAnalysis = result.Value ?? [],
                ThresholdAnalysisRequested = true,
                ThresholdAnalysisErrorMessage = result.ErrorMessage
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

    private static BotH2026ThresholdAnalysisFiltersViewModel CreateThresholdDefaults(
        BotH2026FiltersViewModel filters) => new()
    {
        AsOfUtc = filters.AsOfUtc,
        ConfigurationVersion = filters.ConfigurationVersion,
        MarketType = NormalizeOptional(filters.MarketType),
        Selection = NormalizeOptional(filters.Selection)
    };

    private static void NormalizeThresholdFilters(BotH2026ThresholdAnalysisFiltersViewModel filters)
    {
        filters.AnalysisVersion = string.IsNullOrWhiteSpace(filters.AnalysisVersion)
            ? "bot-h-threshold-what-if-1.0.0"
            : filters.AnalysisVersion.Trim();
        filters.ConfigurationVersion = NormalizeOptional(filters.ConfigurationVersion);
        filters.MarketType = NormalizeOptional(filters.MarketType);
        filters.Selection = NormalizeOptional(filters.Selection);
    }

    private static string? ValidateThresholdFilters(BotH2026ThresholdAnalysisFiltersViewModel filters)
    {
        if (filters.AnalysisVersion != "bot-h-threshold-what-if-1.0.0")
            return "La versión del análisis no es compatible con este laboratorio.";
        if (filters.MarketType is not null
            && filters.MarketType is not ("TotalCorners" or "HomeTeamCorners" or "AwayTeamCorners"))
            return "El mercado elegido no es compatible con H2026.";
        if (filters.Selection is not null && filters.Selection is not ("Over" or "Under"))
            return "La selección debe ser Over o Under.";
        if (!InRange(filters.MinimumFinalProbability, 0m, 1m)
            || !InRange(filters.MinimumFinalEdge, 0m, 1m)
            || !InRange(filters.MinimumFinalExpectedValue, 0m, 10m)
            || !InRange(filters.MinimumDataQualityScore, 0m, 1m)
            || !InRange(filters.MinimumContextAgreementScore, 0m, 1m))
            return "Probabilidad, edge, EV, calidad o contexto están fuera de rango.";
        if (!InRange(filters.MinimumOdds, 1.01m, 10m)
            || !InRange(filters.MaximumOdds, 1.01m, 10m)
            || filters.MaximumOdds < filters.MinimumOdds)
            return "El rango de cuotas no es válido.";
        if (!InRange(filters.DevelopmentFraction, 0.50m, 0.90m))
            return "El porcentaje de desarrollo debe estar entre 50% y 90%.";
        return null;
    }

    private static bool InRange(decimal value, decimal minimum, decimal maximum) =>
        value >= minimum && value <= maximum;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
