using AutomatedCornersBot.Api;
using CornersPrediction.Application.Automation;
using CornersPredictionApi.ApiFootball;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.RecommendationJobs;

public sealed class RecommendationJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RecommendationJobOptions _options;
    private readonly ILogger<RecommendationJobWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private DateTime _nextRecurringAtUtc = DateTime.MinValue;
    private readonly IHostEnvironment _environment;
    private bool _recoveredLocalLeases;

    public RecommendationJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<RecommendationJobOptions> options,
        ILogger<RecommendationJobWorker> logger,
        IHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Recommendation job worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Clamp(_options.PollIntervalSeconds, 1, 60));
        var leaseDuration = TimeSpan.FromMinutes(Math.Clamp(_options.LeaseMinutes, 5, 1440));
        _logger.LogInformation("Recommendation job worker {WorkerId} started.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var schema = scope.ServiceProvider.GetRequiredService<SqlAutomationRepository>();
                await schema.EnsureSchemaAsync(stoppingToken);
                var repository = scope.ServiceProvider.GetRequiredService<IRecommendationJobRepository>();
                if (!_recoveredLocalLeases && _environment.IsDevelopment())
                {
                    await RecoverLocalLeasesAsync(repository, stoppingToken);
                    _recoveredLocalLeases = true;
                }
                var job = await repository.TryClaimNextAsync(_workerId, leaseDuration, stoppingToken);
                if (job is null)
                {
                    await EnqueueRecurringJobIfDueAsync(scope.ServiceProvider, stoppingToken);
                    job = await repository.TryClaimNextAsync(_workerId, leaseDuration, stoppingToken);
                }
                if (job is null)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                await ProcessBatchAsync(
                    scope.ServiceProvider,
                    repository,
                    job,
                    leaseDuration,
                    stoppingToken);
                // Finish the queued run before refreshing quotes for future work.
                // Refreshing between its batches adds minutes and changes the
                // ordered input while the job is still paging through it.
                var updatedJob = await repository.GetAsync(job.RecommendationJobId, stoppingToken);
                if (updatedJob?.IsTerminal == true)
                    await EnqueueRecurringJobIfDueAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Recommendation job worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Recommendation job worker {WorkerId} stopped.", _workerId);
    }

    private async Task RecoverLocalLeasesAsync(IRecommendationJobRepository repository, CancellationToken token)
    {
        foreach (var lease in await repository.GetLocalLeasesAsync(Environment.MachineName, token))
        {
            var parts = lease.LeaseOwner.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var processId) || processId <= 0)
                continue;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (!process.HasExited) continue;
            }
            catch (ArgumentException) { /* The old local process no longer exists. */ }
            catch (System.ComponentModel.Win32Exception) { continue; }
            await repository.ReleaseAsync(lease.RecommendationJobId, lease.LeaseOwner, token);
            _logger.LogInformation("Recovered recommendation job {JobId} from exited local process {ProcessId}.",
                lease.RecommendationJobId, processId);
        }
    }

    private async Task ProcessBatchAsync(
        IServiceProvider services,
        IRecommendationJobRepository repository,
        RecommendationJobDto job,
        TimeSpan leaseDuration,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Recommendation job {JobId} processing batch {Batch}/{TotalBatches}. Bots={Bots}, Markets={Markets}",
            job.RecommendationJobId,
            job.NextBatchNumber,
            job.TotalBatches,
            string.Join(',', job.BotKeys),
            string.Join(',', job.MarketFamilies));

        using var batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = MaintainLeaseAsync(
            repository,
            job,
            leaseDuration,
            batchCancellation,
            stoppingToken);

        try
        {
            var historicalBackfill = job.Mode.Equals(
                RecommendationJobModes.HistoricalBackfill,
                StringComparison.OrdinalIgnoreCase);
            var service = services.GetRequiredService<AutomatedCornersSelectionService>();
            var response = await service.RunAsync(
                new RunAutomatedCornersRequest(
                    DateFrom: job.DateFrom,
                    DateTo: job.DateTo,
                    Stake: null,
                    MinEdge: null,
                    MinExpectedValue: null,
                    MinDistanceToLine: null,
                    MaxContextDifference: null,
                    DryRun: false,
                    AllowModelDisagreement: null,
                    League: null,
                    ExcludeExistingSelections: false,
                    BatchNumber: job.NextBatchNumber,
                    BatchSize: job.BatchSize,
                    RunBotC: job.BotKeys.Contains("C2026", StringComparer.OrdinalIgnoreCase),
                    HistoricalBacktest: false,
                    OnlyBotC: false,
                    MarketFamilies: string.Join(',', job.MarketFamilies),
                    HistoricalBackfill: historicalBackfill,
                    BotKeys: string.Join(',', job.BotKeys)),
                batchCancellation.Token,
                async (progress, token) =>
                {
                    try
                    {
                        await repository.ReportActivityAsync(job.RecommendationJobId, _workerId,
                            progress.Stage, progress.TotalBatches, progress.CompletedMatches,
                            progress.TotalMatches, token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogWarning(exception, "Could not report progress for job {JobId}", job.RecommendationJobId);
                    }
                });

            if (batchCancellation.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Recommendation job {JobId} batch {Batch} stopped because the job was cancelled or its lease was lost.",
                    job.RecommendationJobId,
                    job.NextBatchNumber);
                return;
            }

            var updated = await repository.CompleteBatchAsync(
                job.RecommendationJobId,
                _workerId,
                new RecommendationJobBatchProgress(
                    job.NextBatchNumber,
                    response.TotalBatches,
                    response.RunId,
                    response.SelectedMatches,
                    response.InsertedRows,
                    response.UpdatedRows,
                    response.SkippedMatches,
                    response.ErrorMatches,
                    BuildBatchErrorSummary(response)),
                stoppingToken);

            _logger.LogInformation(
                "Recommendation job {JobId} batch {Batch} finished. Status={Status}, Selected={Selected}, Inserted={Inserted}, Updated={Updated}, Errors={Errors}",
                job.RecommendationJobId,
                job.NextBatchNumber,
                updated?.Status ?? "CancelledOrLeaseLost",
                response.SelectedMatches,
                response.InsertedRows,
                response.UpdatedRows,
                response.ErrorMatches);

            if (updated?.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true &&
                _options.ReconcileBotPicksAfterCompletion)
            {
                await TryReconcileBotPicksAsync(services, job, stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await repository.ReleaseAsync(job.RecommendationJobId, _workerId, releaseTimeout.Token);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Could not release job {JobId} during shutdown; its lease will expire.", job.RecommendationJobId);
            }
            throw new OperationCanceledException(stoppingToken);
        }
        catch (OperationCanceledException) when (batchCancellation.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Recommendation job {JobId} batch {Batch} acknowledged cancellation or lease loss.",
                job.RecommendationJobId,
                job.NextBatchNumber);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Recommendation job {JobId} batch {Batch} failed.",
                job.RecommendationJobId,
                job.NextBatchNumber);
            await TryRecordFailureAsync(repository, job, exception.Message);
        }
        finally
        {
            batchCancellation.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    internal static string? BuildBatchErrorSummary(AutomatedRunResponse response)
    {
        if (response.ErrorMatches == 0) return null;
        var summary = string.Join("\n", response.Errors.Select(error =>
            $"{error.HomeTeam} vs {error.AwayTeam}: {error.Error}").Distinct().Take(5));
        if (string.IsNullOrWhiteSpace(summary)) summary = $"El lote terminó con {response.ErrorMatches} errores.";
        return summary.Length <= 1900 ? summary : summary[..1900];
    }

    private async Task MaintainLeaseAsync(
        IRecommendationJobRepository repository,
        RecommendationJobDto job,
        TimeSpan leaseDuration,
        CancellationTokenSource batchCancellation,
        CancellationToken stoppingToken)
    {
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Clamp(_options.HeartbeatSeconds, 5, 300));

        try
        {
            using var timer = new PeriodicTimer(heartbeatInterval);
            while (await timer.WaitForNextTickAsync(batchCancellation.Token))
            {
                bool renewed;
                try
                {
                    renewed = await repository.RenewLeaseAsync(
                        job.RecommendationJobId,
                        _workerId,
                        leaseDuration,
                        batchCancellation.Token);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException &&
                    !stoppingToken.IsCancellationRequested)
                {
                    // The original lease is still valid. Retry on the next tick
                    // instead of cancelling useful work after one transient SQL error.
                    _logger.LogWarning(
                        exception,
                        "Recommendation job {JobId} heartbeat failed; it will retry on the next tick.",
                        job.RecommendationJobId);
                    continue;
                }

                if (renewed)
                {
                    continue;
                }

                _logger.LogInformation(
                    "Recommendation job {JobId} no longer owns its lease; cancelling the active batch.",
                    job.RecommendationJobId);
                batchCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (batchCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task TryReconcileBotPicksAsync(
        IServiceProvider services,
        RecommendationJobDto job,
        CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(GetChileNow());
            var reconciliation = await services
                .GetRequiredService<ApiFootballBotPickReconciliationService>()
                .ReconcileAsync(
                    new ApiFootballBotPickReconciliationRequest(
                        // Revisit every historical pending pick. API-Football may
                        // publish or correct advanced statistics after the bot job
                        // that originally created the recommendation.
                        DateFrom: null,
                        DateTo: today,
                        MaxSelections: Math.Clamp(_options.ReconciliationMaxSelections, 1, 20000),
                        DryRun: false),
                    cancellationToken);
            _logger.LogInformation(
                "Automatic Bot Pick reconciliation after job {JobId}: Matched={Matched}, Synced={Synced}, Settled={Settled}, Pending={Pending}",
                job.RecommendationJobId,
                reconciliation.MatchedSelections,
                reconciliation.SyncedFixtures,
                reconciliation.FinalSettled,
                reconciliation.StillPending);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The recommendation job is already complete. Keep settlement retryable
            // from the global web action instead of marking the bot run as failed.
            _logger.LogWarning(
                exception,
                "Recommendation job {JobId} completed, but automatic Bot Pick reconciliation needs a retry",
                job.RecommendationJobId);
        }
    }

    private async Task EnqueueRecurringJobIfDueAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var recurring = _options.Recurring;
        if (!recurring.Enabled || DateTime.UtcNow < _nextRecurringAtUtc)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(recurring.IntervalMinutes, 5, 10080));
        var today = DateOnly.FromDateTime(GetChileNow());
        var useCase = services.GetRequiredService<IRecommendationJobsUseCase>();
        var botKeys = await ResolveRecurringBotKeysAsync(services, recurring, cancellationToken);
        if (botKeys.Length == 0)
        {
            _nextRecurringAtUtc = DateTime.UtcNow.Add(interval);
            _logger.LogWarning(
                "Recurring recommendation job was not enqueued because no enabled, non-retired bots were found. Next check at {NextRunUtc}.",
                _nextRecurringAtUtc);
            return;
        }

        var expectedDateTo = today.AddDays(Math.Clamp(recurring.LookAheadDays, 0, 30));
        var scheduledName = $"Live automático {today:yyyy-MM-dd}";
        var latestEquivalent = (await useCase.ListAsync(200, cancellationToken))
            .FirstOrDefault(job =>
                job.Name.Equals(scheduledName, StringComparison.OrdinalIgnoreCase) &&
                job.Mode.Equals(RecommendationJobModes.Live, StringComparison.OrdinalIgnoreCase) &&
                job.DateFrom == today &&
                job.DateTo == expectedDateTo &&
                HaveSameValues(job.BotKeys, botKeys) &&
                HaveSameValues(job.MarketFamilies, recurring.MarketFamilies));
        if (latestEquivalent is not null && latestEquivalent.CreatedAtUtc.Add(interval) > DateTime.UtcNow)
        {
            _nextRecurringAtUtc = latestEquivalent.CreatedAtUtc.Add(interval);
            return;
        }

        if (recurring.RefreshOddsBeforeEnqueue)
        {
            await RefreshRecurringOddsBestEffortAsync(services, cancellationToken);
        }

        var job = await useCase.EnqueueAsync(
            new CreateRecommendationJobCommand(
                DateFrom: today,
                DateTo: expectedDateTo,
                Name: scheduledName,
                BotKeys: botKeys,
                MarketFamilies: recurring.MarketFamilies,
                Mode: RecommendationJobModes.Live,
                BatchSize: recurring.BatchSize,
                MaxAttempts: recurring.MaxAttempts),
            cancellationToken);
        _nextRecurringAtUtc = DateTime.UtcNow.Add(interval);
        _logger.LogInformation(
            "Recurring recommendation job {JobId} is {Status}; next schedule check at {NextRunUtc}.",
            job.RecommendationJobId,
            job.Status,
            _nextRecurringAtUtc);
    }

    private static async Task<string[]> ResolveRecurringBotKeysAsync(
        IServiceProvider services,
        RecurringRecommendationJobOptions recurring,
        CancellationToken cancellationToken)
    {
        var configuredBotKeys = recurring.BotKeys
            .Where(botKey => !string.IsNullOrWhiteSpace(botKey))
            .Select(RecommendationBotDefinitionsUseCase.NormalizeBotKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configuredBotKeys.Length > 0)
        {
            return configuredBotKeys;
        }

        var definitions = await services
            .GetRequiredService<IRecommendationBotDefinitionRepository>()
            .GetAllAsync(cancellationToken);
        return ResolveEnabledBotKeys(definitions);
    }

    internal static string[] ResolveEnabledBotKeys(
        IReadOnlyCollection<RecommendationBotDefinitionDto> definitions) =>
        definitions
            .Where(definition =>
                definition.IsEnabled &&
                !RecommendationBotLifecycle.IsRetired(definition.BotKey))
            .Select(definition => RecommendationBotDefinitionsUseCase.NormalizeBotKey(definition.BotKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HaveSameValues(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right) =>
        left.Count == right.Count &&
        left.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(right);

    private async Task RefreshRecurringOddsBestEffortAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var pipeline = services.GetRequiredService<ICornersPipelineService>();
        var pinnacleTask = RefreshOddsSourceBestEffortAsync(
            "Pinnacle",
            pipeline.RunPinnacleOddsAsync,
            cancellationToken);
        var betanoTask = RefreshOddsSourceBestEffortAsync(
            "Betano",
            pipeline.RunBetanoOddsAsync,
            cancellationToken);

        var results = await Task.WhenAll(pinnacleTask, betanoTask);
        var pinnacle = results[0];
        var betano = results[1];

        if (pinnacle?.IsSuccess != true && betano?.IsSuccess != true)
        {
            _logger.LogWarning(
                "Both odds refreshes failed before the recurring recommendation job. " +
                "The job will still be enqueued; the odds freshness gate remains authoritative.");
            return;
        }

        _logger.LogInformation(
            "Pre-job odds refresh completed. Pinnacle={PinnacleStatus}, Betano={BetanoStatus}.",
            pinnacle?.Status ?? "Exception",
            betano?.Status ?? "Exception");
    }

    private async Task<CornersPipelineStepResult?> RefreshOddsSourceBestEffortAsync(
        string source,
        Func<CancellationToken, Task<CornersPipelineStepResult>> refresh,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await refresh(cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "{Source} odds refresh failed before the recurring recommendation job: {Message}",
                    source,
                    result.Message);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "{Source} odds refresh raised an unexpected error before the recurring recommendation job.",
                source);
            return null;
        }
    }

    private async Task TryRecordFailureAsync(
        IRecommendationJobRepository repository,
        RecommendationJobDto job,
        string error)
    {
        try
        {
            await repository.RecordFailureAsync(
                job.RecommendationJobId,
                _workerId,
                error,
                CancellationToken.None);
        }
        catch (Exception persistenceException)
        {
            _logger.LogError(
                persistenceException,
                "Could not record failure for recommendation job {JobId}.",
                job.RecommendationJobId);
        }
    }

    private static DateTime GetChileNow()
    {
        foreach (var timeZoneId in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateTime.Now;
    }
}
