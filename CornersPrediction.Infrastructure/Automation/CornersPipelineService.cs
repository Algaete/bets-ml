using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation;
using CornersPrediction.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CornersPrediction.Infrastructure.Automation;

public sealed class CornersPipelineService : ICornersPipelineService
{
    internal const string CornersDataClientName = "CornersAutomationDataApi";
    internal const string CornersBotClientName = "CornersAutomationBotApi";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CornersAutomationOptions _options;
    private readonly ILogger<CornersPipelineService> _logger;

    public CornersPipelineService(
        IHttpClientFactory httpClientFactory,
        IOptions<CornersAutomationOptions> options,
        ILogger<CornersPipelineService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<CornersPipelineStepResult> RunMatchHistoryAsync(int days, CancellationToken cancellationToken)
    {
        var effectiveDays = NormalizeDays(days);
        var (fromDate, toDate) = BuildDateRange(effectiveDays);
        var query = BuildQueryString(
            ("fromDate", fromDate),
            ("toDate", toDate));

        return ExecuteStepAsync(
            stepKey: "match-history",
            stepName: "MatchHistory",
            days: effectiveDays,
            timeout: TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<EspnMultiLeagueBatchResponse>(
                    CornersDataClientName,
                    $"/api/EspnMatchHistoryScrapping/sincronizar/rango-fechas{query}",
                    body: null,
                    token);

                return new CornersPipelineStepResult
                {
                    Message = response.Model.Message,
                    Discovered = response.Model.TotalDiscovered,
                    Processed = response.Model.TotalProcessed,
                    Inserted = response.Model.Inserted,
                    Updated = response.Model.Updated,
                    Duplicates = response.Model.Duplicates,
                    Skipped = response.Model.Skipped,
                    Errors = response.Model.Errors,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public Task<CornersPipelineStepResult> RunWorldCupMatchHistoryAsync(int days, CancellationToken cancellationToken)
    {
        var effectiveDays = NormalizeDays(days);
        var (fromDate, toDate) = BuildDateRange(effectiveDays);
        var query = BuildQueryString(
            ("league", "fifa.world"),
            ("fromDate", fromDate),
            ("toDate", toDate),
            ("take", _options.MatchHistoryTake),
            ("parallelism", _options.MatchHistoryParallelism),
            ("dryRun", false),
            ("onlyCompleted", true),
            ("backwards", true),
            ("dbLeague", "Copa del Mundo"),
            ("isKnockout", true),
            ("unknownFormationIfMissing", false));

        return ExecuteStepAsync(
            stepKey: "world-cup-match-history",
            stepName: "WorldCupMatchHistory",
            days: effectiveDays,
            timeout: TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<EspnHistoryBatchResponse>(
                    CornersDataClientName,
                    $"/api/EspnMatchHistoryScrapping/scrape-date-range{query}",
                    body: null,
                    token);

                return new CornersPipelineStepResult
                {
                    Message = response.Model.Message,
                    Discovered = response.Model.TotalDiscovered,
                    Processed = response.Model.TotalProcessed,
                    Inserted = response.Model.Inserted,
                    Updated = response.Model.Updated,
                    Duplicates = response.Model.Duplicates,
                    Skipped = response.Model.Skipped,
                    Errors = response.Model.Errors,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public Task<CornersPipelineStepResult> RunUpcomingMatchesAsync(int days, CancellationToken cancellationToken)
    {
        var effectiveDays = NormalizeDays(days, _options.DefaultUpcomingDays);
        var query = BuildQueryString(("days", effectiveDays));

        return ExecuteStepAsync(
            stepKey: "upcoming-matches",
            stepName: "UpcomingMatches",
            days: effectiveDays,
            timeout: TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<UpcomingMatchesSyncResponse>(
                    CornersDataClientName,
                    $"/api/partidos/sincronizar/proximos{query}",
                    body: null,
                    token);

                return new CornersPipelineStepResult
                {
                    Message = response.Model.Message,
                    Discovered = response.Model.TotalDescubiertos,
                    Processed = response.Model.TotalProcesados,
                    Upserted = response.Model.TotalProcesados,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public Task<CornersPipelineStepResult> RunPinnacleOddsAsync(CancellationToken cancellationToken)
    {
        var query = BuildQueryString(
            ("take", _options.PinnacleTake),
            ("persist", true));

        return ExecuteStepAsync(
            stepKey: "pinnacle-odds",
            stepName: "PinnacleOdds",
            days: null,
            timeout: TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<UpcomingOddsResponse>(
                    CornersDataClientName,
                    $"/api/PinnacleOddsScrapping/scrape-upcoming-football{query}",
                    body: null,
                    token);

                return new CornersPipelineStepResult
                {
                    Message = response.Model.Message,
                    Discovered = response.Model.TotalDiscovered,
                    Processed = response.Model.TotalProcessed,
                    Upserted = response.Model.PersistedCount,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public Task<CornersPipelineStepResult> RunBetanoOddsAsync(CancellationToken cancellationToken)
    {
        var query = BuildQueryString(
            ("take", _options.BetanoTake),
            ("persist", true));

        return ExecuteStepAsync(
            stepKey: "betano-odds",
            stepName: "BetanoOdds",
            days: null,
            timeout: TimeSpan.FromSeconds(_options.BetanoTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<UpcomingOddsResponse>(
                    CornersDataClientName,
                    $"/api/BetanoOddsScrapping/scrape-upcoming-football{query}",
                    body: null,
                    token);

                return new CornersPipelineStepResult
                {
                    Message = response.Model.Message,
                    Discovered = response.Model.TotalDiscovered,
                    Processed = response.Model.TotalProcessed,
                    Upserted = response.Model.PersistedCount,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public Task<CornersPipelineStepResult> RunBotsAsync(bool excludeExistingSelections, CancellationToken cancellationToken)
    {
        var request = new
        {
            ExcludeExistingSelections = excludeExistingSelections
        };

        return ExecuteStepAsync(
            stepKey: "run-bots",
            stepName: "RunBots",
            days: null,
            timeout: TimeSpan.FromSeconds(_options.BotTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<AutomatedRunResponse>(
                    CornersBotClientName,
                    "/api/automated-corners/run",
                    request,
                    token);

                var botACount = response.Model.Selections.Count(selection =>
                    selection.Selection.AutomationVersion.EndsWith("-A", StringComparison.OrdinalIgnoreCase));
                var botBCount = response.Model.Selections.Count(selection =>
                    selection.Selection.AutomationVersion.EndsWith("-B", StringComparison.OrdinalIgnoreCase));
                var missingHistoryMatches = response.Model.Skipped
                    .Where(item => IsMissingHistoryReason(item.Reason))
                    .Select(item => new MissingHistoryMatch(
                        item.League,
                        item.HomeTeam,
                        item.AwayTeam,
                        item.MatchDate,
                        item.Reason))
                    .ToArray();

                return new CornersPipelineStepResult
                {
                    Message = $"RunId: {response.Model.RunId}",
                    Discovered = response.Model.TotalOddsRows,
                    Processed = response.Model.TotalMatches,
                    Inserted = response.Model.InsertedRows,
                    Updated = response.Model.UpdatedRows,
                    Skipped = response.Model.SkippedMatches,
                    Errors = response.Model.ErrorMatches,
                    RecommendationsGenerated = response.Model.SelectedMatches,
                    BotACount = botACount,
                    BotBCount = botBCount,
                    MissingHistoryMatches = missingHistoryMatches,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public async Task<CornersPipelineRunResult> RunFullPipelineAsync(
        RunFullPipelineCommand command,
        CancellationToken cancellationToken)
    {
        var matchHistoryDays = NormalizeDays(command.MatchHistoryDays);
        var upcomingDays = NormalizeDays(command.UpcomingDays, _options.DefaultUpcomingDays);
        var startedAtUtc = DateTime.UtcNow;
        var steps = new List<CornersPipelineStepResult>();

        var matchHistory = await RunMatchHistoryAsync(matchHistoryDays, cancellationToken);
        steps.Add(matchHistory);

        var worldCup = await RunWorldCupMatchHistoryAsync(matchHistoryDays, cancellationToken);
        steps.Add(worldCup);

        var upcoming = await RunUpcomingMatchesAsync(upcomingDays, cancellationToken);
        steps.Add(upcoming);

        var pinnacle = await RunPinnacleOddsAsync(cancellationToken);
        steps.Add(pinnacle);

        var betano = await RunBetanoOddsAsync(cancellationToken);
        steps.Add(betano);

        var hasCriticalDataFailure = !matchHistory.IsSuccess || !worldCup.IsSuccess || !upcoming.IsSuccess;
        var hasOddsSource = pinnacle.IsSuccess || betano.IsSuccess;

        if (hasCriticalDataFailure || !hasOddsSource)
        {
            var reason = hasCriticalDataFailure
                ? "Bot execution skipped because one or more critical sync steps failed."
                : "Bot execution skipped because both odds scrapers failed.";

            steps.Add(CreateSkippedStep("run-bots", "RunBots", reason));
            return BuildPipelineResult(startedAtUtc, matchHistoryDays, upcomingDays, steps);
        }

        steps.Add(await RunBotsAsync(command.ExcludeExistingSelections, cancellationToken));
        return BuildPipelineResult(startedAtUtc, matchHistoryDays, upcomingDays, steps);
    }

    private async Task<CornersPipelineStepResult> ExecuteStepAsync(
        string stepKey,
        string stepName,
        int? days,
        TimeSpan timeout,
        Func<CancellationToken, Task<CornersPipelineStepResult>> operation,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var result = await operation(timeoutSource.Token);
            var completedAtUtc = DateTime.UtcNow;

            return result with
            {
                StepKey = stepKey,
                StepName = stepName,
                Days = days,
                Status = CornersPipelineStatuses.Success,
                IsSuccess = true,
                TimedOut = false,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds
            };
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Timeout while running pipeline step {StepKey}", stepKey);
            return CreateFailureStep(
                stepKey,
                stepName,
                days,
                startedAtUtc,
                timedOut: true,
                message: "The request timed out while waiting for the external API.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to run pipeline step {StepKey}", stepKey);
            return CreateFailureStep(
                stepKey,
                stepName,
                days,
                startedAtUtc,
                timedOut: false,
                message: exception.Message);
        }
    }

    private async Task<(T Model, JsonElement Raw)> PostAsync<T>(
        string clientName,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildHttpErrorMessage(relativeUrl, response.StatusCode, content));
        }

        var safeContent = string.IsNullOrWhiteSpace(content) ? "{}" : content;
        using var document = JsonDocument.Parse(safeContent);
        var raw = document.RootElement.Clone();
        var model = JsonSerializer.Deserialize<T>(safeContent, SerializerOptions);

        return model is null
            ? throw new InvalidOperationException($"External API returned an empty payload for '{relativeUrl}'.")
            : (model, raw);
    }

    private static CornersPipelineStepResult CreateFailureStep(
        string stepKey,
        string stepName,
        int? days,
        DateTime startedAtUtc,
        bool timedOut,
        string message)
    {
        var completedAtUtc = DateTime.UtcNow;
        return new CornersPipelineStepResult
        {
            StepKey = stepKey,
            StepName = stepName,
            Days = days,
            Status = CornersPipelineStatuses.Failed,
            IsSuccess = false,
            TimedOut = timedOut,
            Message = timedOut ? "Timed out" : "Execution failed",
            ErrorMessage = message,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds
        };
    }

    private static CornersPipelineStepResult CreateSkippedStep(
        string stepKey,
        string stepName,
        string reason)
    {
        var timestamp = DateTime.UtcNow;
        return new CornersPipelineStepResult
        {
            StepKey = stepKey,
            StepName = stepName,
            Status = CornersPipelineStatuses.Skipped,
            IsSuccess = false,
            TimedOut = false,
            Message = reason,
            StartedAtUtc = timestamp,
            CompletedAtUtc = timestamp,
            DurationMs = 0
        };
    }

    private static CornersPipelineRunResult BuildPipelineResult(
        DateTime startedAtUtc,
        int matchHistoryDays,
        int upcomingDays,
        IReadOnlyList<CornersPipelineStepResult> steps)
    {
        var completedAtUtc = DateTime.UtcNow;
        var failedSteps = steps.Count(step => step.Status == CornersPipelineStatuses.Failed);
        var timedOutSteps = steps.Count(step => step.TimedOut);
        var skippedSteps = steps.Count(step => step.Status == CornersPipelineStatuses.Skipped);
        var successfulSteps = steps.Count(step => step.IsSuccess);

        var status = failedSteps switch
        {
            0 when skippedSteps == 0 => CornersPipelineStatuses.Success,
            0 => CornersPipelineStatuses.PartialSuccess,
            _ when successfulSteps > 0 => CornersPipelineStatuses.PartialSuccess,
            _ => CornersPipelineStatuses.Failed
        };

        return new CornersPipelineRunResult
        {
            PipelineName = "Full robot pipeline",
            Status = status,
            MatchHistoryDays = matchHistoryDays,
            UpcomingDays = upcomingDays,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = (long)(completedAtUtc - startedAtUtc).TotalMilliseconds,
            SuccessfulSteps = successfulSteps,
            FailedSteps = failedSteps,
            TimedOutSteps = timedOutSteps,
            RecommendationsGenerated = steps.Sum(step => step.RecommendationsGenerated ?? 0),
            Steps = steps
        };
    }

    private static string BuildHttpErrorMessage(string relativeUrl, System.Net.HttpStatusCode statusCode, string content)
    {
        var body = string.IsNullOrWhiteSpace(content)
            ? "No response body."
            : content.Trim();

        if (body.Length > 1_000)
        {
            body = body[..1_000] + "...";
        }

        return $"External API call '{relativeUrl}' failed with {(int)statusCode} ({statusCode}). {body}";
    }

    private static int NormalizeDays(int days, int defaultValue = 7)
    {
        if (days <= 0)
        {
            return defaultValue;
        }

        return Math.Min(days, 30);
    }

    private static (DateOnly FromDate, DateOnly ToDate) BuildDateRange(int days)
    {
        var end = DateOnly.FromDateTime(DateTime.Today);
        var start = end.AddDays(-(days - 1));
        return (start, end);
    }

    private static string BuildQueryString(params (string Key, object? Value)[] items)
    {
        var query = items
            .Where(item => item.Value is not null)
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(FormatQueryValue(item.Value!))}")
            .ToArray();

        return query.Length == 0 ? string.Empty : "?" + string.Join("&", query);
    }

    private static string FormatQueryValue(object value) =>
        value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static bool IsMissingHistoryReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && (reason.Contains("history", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("context was empty", StringComparison.OrdinalIgnoreCase));

    private sealed record EspnHistoryBatchResponse(
        string? Message,
        int TotalDiscovered,
        int TotalProcessed,
        int Inserted,
        int Updated,
        int Duplicates,
        int Skipped,
        int Errors);

    private sealed record EspnMultiLeagueBatchResponse(
        string? Message,
        int TotalDiscovered,
        int TotalProcessed,
        int Inserted,
        int Updated,
        int Duplicates,
        int Skipped,
        int Errors);

    private sealed record UpcomingMatchesSyncResponse(
        string? Message,
        int TotalDescubiertos,
        int TotalProcesados);

    private sealed record UpcomingOddsResponse(
        string? Message,
        int TotalDiscovered,
        int TotalProcessed,
        int PersistedCount);

    private sealed record AutomatedRunResponse(
        Guid RunId,
        int TotalOddsRows,
        int TotalMatches,
        int SelectedMatches,
        int InsertedRows,
        int UpdatedRows,
        int SkippedMatches,
        int ErrorMatches,
        IReadOnlyList<AutomatedSelectionResult> Selections,
        IReadOnlyList<SkippedMatchResult> Skipped);

    private sealed record SkippedMatchResult(
        string League,
        string HomeTeam,
        string AwayTeam,
        DateTime MatchDate,
        string Reason);

    private sealed record AutomatedSelectionResult(
        string MergeAction,
        PersistedAutomatedSelection Selection);

    private sealed record PersistedAutomatedSelection(
        string AutomationVersion);
}
