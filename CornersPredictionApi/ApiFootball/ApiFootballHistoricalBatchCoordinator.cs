namespace CornersPredictionApi.ApiFootball;

public sealed class ApiFootballHistoricalBatchCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiFootballHistoricalBatchCoordinator> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _stateLock = new();
    private Task? _runningTask;
    private ApiFootballHistoricalBatchState? _runningState;

    public ApiFootballHistoricalBatchCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<ApiFootballHistoricalBatchCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ApiFootballHistoricalBatchState> GetStateAsync(CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_runningTask is { IsCompleted: false } && _runningState is not null)
            {
                return _runningState;
            }
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ApiFootballRepository>();
        return ToState(await repository.GetHistoricalCheckpointAsync(cancellationToken));
    }

    public async Task<ApiFootballHistoricalBatchState> StartAsync(
        ApiFootballHistoricalBatchRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            lock (_stateLock)
            {
                if (_runningTask is { IsCompleted: false } && _runningState is not null)
                {
                    return _runningState;
                }
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ApiFootballRepository>();
            var checkpoint = await repository.GetHistoricalCheckpointAsync(cancellationToken);
            var checkpointMonth = DateOnly.FromDateTime(checkpoint.Month);
            var month = FirstDayOfMonth(request.Month ?? checkpointMonth);
            var offset = request.CompetitionOffset ?? checkpoint.CompetitionOffset;
            var startedAtUtc = DateTime.UtcNow;
            var running = new ApiFootballHistoricalBatchState(
                "Running",
                true,
                month,
                offset,
                month,
                offset,
                startedAtUtc,
                null,
                DiscoveredFixtures: null,
                EligibleCompetitions: null,
                ProcessedCompetitions: null,
                ProcessedFixtures: null,
                Inserted: null,
                Updated: null,
                Skipped: null,
                Errors: null,
                StoppedByQuota: null,
                DailyRemaining: null,
                MinuteRemaining: null,
                Message: "Tanda historica en ejecucion.");

            await repository.SaveHistoricalCheckpointAsync(
                ToCheckpoint(running),
                cancellationToken);

            lock (_stateLock)
            {
                _runningState = running;
                _runningTask = Task.Run(
                    () => RunAsync(request, month, offset, startedAtUtc),
                    CancellationToken.None);
            }

            return running;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task RunAsync(
        ApiFootballHistoricalBatchRequest request,
        DateOnly month,
        int offset,
        DateTime startedAtUtc)
    {
        ApiFootballHistoricalBatchState completed;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ApiFootballSyncService>();
            var repository = scope.ServiceProvider.GetRequiredService<ApiFootballRepository>();
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var result = await service.BulkSyncAsync(
                new ApiFootballBulkSyncRequest(
                    month,
                    monthEnd,
                    offset,
                    request.MaxCompetitions,
                    request.MaxFixturesPerCompetition,
                    request.MaxTotalFixtures,
                    request.MinimumDailyRemaining,
                    DryRun: false,
                    UpdateExisting: true,
                    SyncStandings: true,
                    SyncLineups: false,
                    SeniorMenOnly: true),
                CancellationToken.None);

            var next = CalculateNextCheckpoint(month, offset, request.MaxCompetitions, result);
            completed = new ApiFootballHistoricalBatchState(
                result.Errors > 0 ? "Partial" : result.StoppedByQuota ? "QuotaExhausted" : "Completed",
                false,
                month,
                offset,
                next.Month,
                next.Offset,
                startedAtUtc,
                DateTime.UtcNow,
                result.DiscoveredFixtures,
                result.EligibleCompetitions,
                result.ProcessedCompetitions,
                result.ProcessedFixtures,
                result.Inserted,
                result.Updated,
                result.Skipped,
                result.Errors,
                result.StoppedByQuota,
                result.DailyRemaining,
                result.MinuteRemaining,
                next.Message);

            await repository.SaveHistoricalCheckpointAsync(
                ToCheckpoint(completed with
                {
                    Month = next.Month,
                    CompetitionOffset = next.Offset
                }),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "API-Football historical batch failed for {Month} at offset {Offset}",
                month,
                offset);
            completed = new ApiFootballHistoricalBatchState(
                "Failed",
                false,
                month,
                offset,
                month,
                offset,
                startedAtUtc,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                1,
                null,
                null,
                null,
                exception.Message);

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ApiFootballRepository>();
                await repository.SaveHistoricalCheckpointAsync(
                    ToCheckpoint(completed),
                    CancellationToken.None);
            }
            catch (Exception saveException)
            {
                _logger.LogError(saveException, "Could not persist the failed historical batch state");
            }
        }

        lock (_stateLock)
        {
            _runningState = completed;
        }
    }

    private static (DateOnly Month, int Offset, string Message) CalculateNextCheckpoint(
        DateOnly month,
        int offset,
        int maxCompetitions,
        ApiFootballBulkSyncResult result)
    {
        var last = result.Rows.LastOrDefault();
        var retryLast = last is not null &&
            (last.RequestedFixtures < last.AvailableFixtures ||
             last.Status is "QuotaExceeded" or "Error");
        var advanced = Math.Max(0, result.ProcessedCompetitions - (retryLast ? 1 : 0));
        var nextOffset = offset + advanced;
        var monthComplete = result.EligibleCompetitions == 0 ||
            (!retryLast &&
             result.ProcessedCompetitions == result.EligibleCompetitions &&
             result.EligibleCompetitions < maxCompetitions);

        if (monthComplete)
        {
            var previousMonth = month.AddMonths(-1);
            return (
                previousMonth,
                0,
                $"Mes {month:yyyy-MM} completado. La proxima tanda comenzara en {previousMonth:yyyy-MM}.");
        }

        var reason = result.StoppedByQuota
            ? "Cuota diaria agotada."
            : retryLast
                ? "La ultima competicion quedo parcial."
                : "Se alcanzo el limite de la tanda.";
        return (month, nextOffset, $"{reason} Proxima tanda: {month:yyyy-MM}, offset {nextOffset}.");
    }

    private static ApiFootballHistoricalBatchState ToState(ApiFootballHistoricalCheckpoint checkpoint)
    {
        var month = DateOnly.FromDateTime(checkpoint.Month);
        var status = checkpoint.Status == "Running" ? "Interrupted" : checkpoint.Status;
        var message = checkpoint.Status == "Running"
            ? "La API se reinicio durante la tanda anterior. Puedes ejecutarla nuevamente desde el mismo checkpoint."
            : checkpoint.Message ?? "Lista para ejecutar.";
        return
        new(
            status,
            false,
            month,
            checkpoint.CompetitionOffset,
            month,
            checkpoint.CompetitionOffset,
            checkpoint.StartedAtUtc,
            checkpoint.CompletedAtUtc,
            checkpoint.DiscoveredFixtures,
            checkpoint.EligibleCompetitions,
            checkpoint.ProcessedCompetitions,
            checkpoint.ProcessedFixtures,
            checkpoint.Inserted,
            checkpoint.Updated,
            checkpoint.Skipped,
            checkpoint.Errors,
            checkpoint.StoppedByQuota,
            checkpoint.DailyRemaining,
            checkpoint.MinuteRemaining,
            message);
    }

    private static ApiFootballHistoricalCheckpoint ToCheckpoint(ApiFootballHistoricalBatchState state) =>
        new(
            state.NextMonth.ToDateTime(TimeOnly.MinValue),
            state.NextCompetitionOffset,
            state.Status,
            state.StartedAtUtc,
            state.CompletedAtUtc,
            state.DiscoveredFixtures,
            state.EligibleCompetitions,
            state.ProcessedCompetitions,
            state.ProcessedFixtures,
            state.Inserted,
            state.Updated,
            state.Skipped,
            state.Errors,
            state.StoppedByQuota,
            state.DailyRemaining,
            state.MinuteRemaining,
            state.Message);

    private static DateOnly FirstDayOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static void Validate(ApiFootballHistoricalBatchRequest request)
    {
        if (request.CompetitionOffset < 0)
        {
            throw new ArgumentException("CompetitionOffset cannot be negative.");
        }
        if (request.MaxCompetitions is < 1 or > 1000)
        {
            throw new ArgumentException("MaxCompetitions must be between 1 and 1000.");
        }
        if (request.MaxFixturesPerCompetition is < 1 or > 5000)
        {
            throw new ArgumentException("MaxFixturesPerCompetition must be between 1 and 5000.");
        }
        if (request.MaxTotalFixtures is < 1 or > 20000)
        {
            throw new ArgumentException("MaxTotalFixtures must be between 1 and 20000.");
        }
        if (request.MinimumDailyRemaining is < 0 or > 1000)
        {
            throw new ArgumentException("MinimumDailyRemaining must be between 0 and 1000.");
        }
    }
}
