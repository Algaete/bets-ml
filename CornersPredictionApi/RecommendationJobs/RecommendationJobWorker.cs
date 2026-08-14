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

    public RecommendationJobWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<RecommendationJobOptions> options,
        ILogger<RecommendationJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
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
                await EnqueueRecurringJobIfDueAsync(scope.ServiceProvider, stoppingToken);

                var repository = scope.ServiceProvider.GetRequiredService<IRecommendationJobRepository>();
                var job = await repository.TryClaimNextAsync(_workerId, leaseDuration, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                await ProcessBatchAsync(scope.ServiceProvider, repository, job, stoppingToken);
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

    private async Task ProcessBatchAsync(
        IServiceProvider services,
        IRecommendationJobRepository repository,
        RecommendationJobDto job,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Recommendation job {JobId} processing batch {Batch}/{TotalBatches}. Bots={Bots}, Markets={Markets}",
            job.RecommendationJobId,
            job.NextBatchNumber,
            job.TotalBatches,
            string.Join(',', job.BotKeys),
            string.Join(',', job.MarketFamilies));

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
                stoppingToken);

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
                    response.ErrorMatches),
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await TryRecordFailureAsync(repository, job, "API shutdown interrupted the active batch.");
            throw;
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
        var expectedDateTo = today.AddDays(Math.Clamp(recurring.LookAheadDays, 0, 30));
        var scheduledName = $"Live automático {today:yyyy-MM-dd}";
        var latestEquivalent = (await useCase.ListAsync(200, cancellationToken))
            .FirstOrDefault(job =>
                job.Name.Equals(scheduledName, StringComparison.OrdinalIgnoreCase) &&
                job.Mode.Equals(RecommendationJobModes.Live, StringComparison.OrdinalIgnoreCase) &&
                job.DateFrom == today &&
                job.DateTo == expectedDateTo);
        if (latestEquivalent is not null && latestEquivalent.CreatedAtUtc.Add(interval) > DateTime.UtcNow)
        {
            _nextRecurringAtUtc = latestEquivalent.CreatedAtUtc.Add(interval);
            return;
        }

        var job = await useCase.EnqueueAsync(
            new CreateRecommendationJobCommand(
                DateFrom: today,
                DateTo: expectedDateTo,
                Name: scheduledName,
                BotKeys: recurring.BotKeys,
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
