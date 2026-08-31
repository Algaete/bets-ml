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

        return ExecuteStepAsync(
            stepKey: "match-history",
            stepName: "MatchHistory API-Football",
            days: effectiveDays,
            timeout: TimeSpan.FromSeconds(_options.ApiFootballTimeoutSeconds),
            operation: token => RunApiFootballMatchHistoryAsync(fromDate, toDate, token),
            cancellationToken);
    }

    private async Task<CornersPipelineStepResult> RunApiFootballMatchHistoryAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var request = new ApiFootballBulkSyncRequest(
            DateFrom: fromDate,
            DateTo: toDate,
            CompetitionOffset: 0,
            MaxCompetitions: _options.ApiFootballMaxCompetitions,
            MaxFixturesPerCompetition: _options.ApiFootballMaxFixturesPerCompetition,
            MaxTotalFixtures: _options.ApiFootballMaxTotalFixtures,
            MinimumDailyRemaining: _options.ApiFootballMinimumDailyRemaining,
            DryRun: false,
            UpdateExisting: true,
            SyncStandings: true,
            SyncLineups: false,
            SeniorMenOnly: true);

        var response = await PostAsync<ApiFootballBulkSyncResponse>(
            CornersDataClientName,
            "/api/api-football/bulk-sync",
            request,
            cancellationToken);

        return new CornersPipelineStepResult
        {
            Message = response.Model.StoppedByQuota
                ? $"API-Football stopped by quota. Daily remaining: {response.Model.DailyRemaining ?? "unknown"}."
                : $"API-Football recent history synced. Daily remaining: {response.Model.DailyRemaining ?? "unknown"}.",
            Discovered = response.Model.DiscoveredFixtures,
            Processed = response.Model.ProcessedFixtures,
            Inserted = response.Model.Inserted,
            Updated = response.Model.Updated,
            Skipped = response.Model.Skipped,
            Errors = response.Model.Errors,
            RawResponse = response.Raw
        };
    }

    public Task<CornersPipelineStepResult> RunUpcomingMatchesAsync(int days, CancellationToken cancellationToken)
    {
        var effectiveDays = NormalizeDays(days, _options.DefaultUpcomingDays);
        var dateFrom = DateOnly.FromDateTime(DateTime.Today);
        var dateTo = dateFrom.AddDays(effectiveDays - 1);

        return ExecuteStepAsync(
            stepKey: "upcoming-matches",
            stepName: "UpcomingMatches",
            days: effectiveDays,
            timeout: TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds),
            operation: async token =>
            {
                var response = await PostAsync<ApiFootballUpcomingSyncResponse>(
                    CornersDataClientName,
                    "/api/api-football/sync-upcoming",
                    new ApiFootballUpcomingSyncRequest(dateFrom, dateTo),
                    token);

                return new CornersPipelineStepResult
                {
                    Message = $"API-Football upcoming fixtures synced. " +
                        $"Excluded: {response.Model.ExcludedFixtures}. " +
                        $"Daily remaining: {response.Model.DailyRemaining ?? "unknown"}.",
                    Discovered = response.Model.DiscoveredFixtures,
                    Processed = response.Model.EligibleFixtures,
                    Upserted = response.Model.PersistedFixtures,
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
                    Status = response.Model.PersistenceFailedMatches > 0
                        ? CornersPipelineStatuses.PartialSuccess
                        : CornersPipelineStatuses.Success,
                    Discovered = response.Model.TotalDiscovered,
                    Processed = response.Model.TotalProcessed,
                    Upserted = response.Model.PersistedCount,
                    Skipped = response.Model.PersistenceSkippedMatches,
                    Errors = response.Model.PersistenceFailedMatches,
                    RawResponse = response.Raw
                };
            },
            cancellationToken);
    }

    public async Task<BotOddsAvailability> GetBotOddsAvailabilityAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var effectiveBatchSize = NormalizeBotBatchSize(batchSize);
        var response = await GetAsync<AutomatedOddsAvailabilityResponse>(
            CornersBotClientName,
            $"/api/automated-corners/availability?batchSize={effectiveBatchSize}",
            cancellationToken);

        return new BotOddsAvailability(
            response.Model.DateFrom,
            response.Model.DateTo,
            response.Model.TotalOddsRows,
            response.Model.TotalMatches,
            response.Model.BatchSize,
            response.Model.TotalBatches);
    }

    public Task<CornersPipelineStepResult> RunBotsAsync(
        RunBotsCommand command,
        CancellationToken cancellationToken)
    {
        var batchNumber = Math.Max(1, command.BatchNumber);
        var batchSize = NormalizeBotBatchSize(command.BatchSize);
        var request = new
        {
            command.ExcludeExistingSelections,
            BatchNumber = batchNumber,
            BatchSize = batchSize,
            command.RunBotC,
            command.RunAllEnabledBots
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

                var botCounts = response.Model.BotCounts ??
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var botACount = botCounts.GetValueOrDefault("A");
                var botBCount = botCounts.GetValueOrDefault("B");
                var botCCount = botCounts.GetValueOrDefault("C2026");
                var botSummary = botCounts.Count == 0
                    ? "sin bots habilitados"
                    : string.Join(", ", botCounts
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => $"{pair.Key}: {pair.Value}"));
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
                    Message = response.Model.TotalOddsRows == 0
                        ? $"Lote {batchNumber} sin cuotas. Disponibles: {response.Model.AvailableOddsRows}."
                        : $"Lote {response.Model.BatchNumber}/{response.Model.TotalBatches}: cuotas {response.Model.BatchStart}-{response.Model.BatchEnd} de {response.Model.AvailableOddsRows}. Bots habilitados: {botSummary}. RunId: {response.Model.RunId}",
                    Discovered = response.Model.AvailableOddsRows,
                    Processed = response.Model.TotalMatches,
                    Inserted = response.Model.InsertedRows,
                    Updated = response.Model.UpdatedRows,
                    Skipped = response.Model.SkippedMatches,
                    Errors = response.Model.ErrorMatches,
                    RecommendationsGenerated = response.Model.SelectedMatches,
                    BotACount = botACount,
                    BotBCount = botBCount,
                    BotCCount = botCCount,
                    BotCounts = botCounts,
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

        var upcoming = await RunUpcomingMatchesAsync(upcomingDays, cancellationToken);
        steps.Add(upcoming);

        var oddsRefreshes = await Task.WhenAll(
            RunPinnacleOddsAsync(cancellationToken),
            RunBetanoOddsAsync(cancellationToken));
        var pinnacle = oddsRefreshes[0];
        var betano = oddsRefreshes[1];
        steps.Add(pinnacle);
        steps.Add(betano);

        var hasCriticalDataFailure = !matchHistory.IsSuccess || !upcoming.IsSuccess;
        var hasOddsSource = pinnacle.IsSuccess || betano.IsSuccess;

        if (hasCriticalDataFailure || !hasOddsSource)
        {
            var reason = hasCriticalDataFailure
                ? "Bot execution skipped because one or more critical sync steps failed."
                : "Bot execution skipped because both odds scrapers failed.";

            steps.Add(CreateSkippedStep("run-bots", "RunBots", reason));
            return BuildPipelineResult(startedAtUtc, matchHistoryDays, upcomingDays, steps);
        }

        steps.Add(await RunBotsAsync(
            new RunBotsCommand(
                command.ExcludeExistingSelections,
                command.BotBatchNumber,
                command.BotBatchSize,
                command.RunBotC,
                command.RunAllEnabledBots),
            cancellationToken));
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

            var reportedStatus = result.Status == CornersPipelineStatuses.PartialSuccess
                ? CornersPipelineStatuses.PartialSuccess
                : CornersPipelineStatuses.Success;

            return result with
            {
                StepKey = stepKey,
                StepName = stepName,
                Days = days,
                Status = reportedStatus,
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
            var timeoutMinutes = Math.Max(1, (int)Math.Ceiling(timeout.TotalMinutes));
            return CreateFailureStep(
                stepKey,
                stepName,
                days,
                startedAtUtc,
                timedOut: true,
                message: $"La fuente externa no completo el proceso dentro de {timeoutMinutes} minutos. " +
                    "La ejecucion se cancelo para no dejar el panel esperando indefinidamente.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private async Task<(T Model, JsonElement Raw)> GetAsync<T>(
        string clientName,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        using var response = await client.GetAsync(
            relativeUrl,
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

    private static int NormalizeBotBatchSize(int batchSize) =>
        Math.Clamp(batchSize <= 0 ? 100 : batchSize, 1, 100);

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

    private sealed record ApiFootballBulkSyncRequest(
        DateOnly DateFrom,
        DateOnly DateTo,
        int CompetitionOffset,
        int MaxCompetitions,
        int MaxFixturesPerCompetition,
        int MaxTotalFixtures,
        int MinimumDailyRemaining,
        bool DryRun,
        bool UpdateExisting,
        bool SyncStandings,
        bool SyncLineups,
        bool SeniorMenOnly);

    private sealed record ApiFootballBulkSyncResponse(
        DateOnly DateFrom,
        DateOnly DateTo,
        bool DryRun,
        int DiscoveredFixtures,
        int DiscoveredCompetitions,
        int EligibleCompetitions,
        int ProcessedCompetitions,
        int ProcessedFixtures,
        int Inserted,
        int Updated,
        int Skipped,
        int Errors,
        bool StoppedByQuota,
        string? DailyRemaining,
        string? MinuteRemaining);

    private sealed record ApiFootballUpcomingSyncRequest(
        DateOnly DateFrom,
        DateOnly DateTo);

    private sealed record ApiFootballUpcomingSyncResponse(
        int DiscoveredFixtures,
        int EligibleFixtures,
        int ExcludedFixtures,
        int PersistedFixtures,
        string? DailyRemaining,
        string? MinuteRemaining);

    private sealed record UpcomingOddsResponse(
        string? Message,
        int TotalDiscovered,
        int TotalProcessed,
        int PersistedCount,
        int PersistenceSkippedMatches = 0,
        int PersistenceFailedMatches = 0);

    private sealed record AutomatedRunResponse(
        Guid RunId,
        int AvailableOddsRows,
        int BatchNumber,
        int BatchSize,
        int BatchStart,
        int BatchEnd,
        int TotalBatches,
        int TotalOddsRows,
        int TotalMatches,
        int SelectedMatches,
        int InsertedRows,
        int UpdatedRows,
        int SkippedMatches,
        int ErrorMatches,
        IReadOnlyDictionary<string, int>? BotCounts,
        IReadOnlyList<AutomatedSelectionResult> Selections,
        IReadOnlyList<SkippedMatchResult> Skipped);

    private sealed record AutomatedOddsAvailabilityResponse(
        DateOnly DateFrom,
        DateOnly DateTo,
        int TotalOddsRows,
        int TotalMatches,
        int BatchSize,
        int TotalBatches);

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
