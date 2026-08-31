using CornersPrediction.Application.FootballIntelligence;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class UpcomingFixtureIntelligenceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FootballIntelligenceOptions _options;
    private readonly ILogger<UpcomingFixtureIntelligenceWorker> _logger;

    public UpcomingFixtureIntelligenceWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<FootballIntelligenceOptions> options,
        ILogger<UpcomingFixtureIntelligenceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.WorkerEnabled)
        {
            _logger.LogInformation(
                "Pre-match intelligence worker is disabled. Enabled={Enabled}, WorkerEnabled={WorkerEnabled}",
                _options.Enabled,
                _options.WorkerEnabled);
            return;
        }

        await RunCycleSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(_options.WorkerPollMinutes, 1, 60)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCycleSafelyAsync(stoppingToken);
    }

    private async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await services.GetRequiredService<FootballIntelligenceSchemaInitializer>()
                .EnsureReadyAsync(cancellationToken);
            var fixtureRepository = services.GetRequiredService<IUpcomingIntelligenceFixtureRepository>();
            var snapshotRepository = services.GetRequiredService<IIntelligenceSnapshotRepository>();
            var intelligenceService = services.GetRequiredService<IMatchIntelligenceService>();
            var now = DateTime.UtcNow;
            var fixtures = await fixtureRepository.GetAsync(
                now.AddMinutes(-5),
                now.AddHours(Math.Clamp(_options.FixtureLookAheadHours, 1, 168)),
                cancellationToken);

            await Parallel.ForEachAsync(
                fixtures,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(_options.MaxConcurrentFixtures, 1, 8),
                    CancellationToken = cancellationToken
                },
                async (fixture, token) =>
                {
                    try
                    {
                        var latest = await snapshotRepository.GetLatestPairAsync(
                            fixture.FixtureId,
                            now,
                            token);
                        var latestCutoff = new[] { latest?.Home?.CutoffAtUtc, latest?.Away?.CutoffAtUtc }
                            .Where(value => value.HasValue)
                            .Select(value => value!.Value)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max();
                        var latestDueStage = _options.CutoffsMinutesBeforeKickoff
                            .Where(minutes => minutes >= 0)
                            .Select(minutes => EnsureUtc(fixture.KickoffUtc).AddMinutes(-minutes))
                            .Where(stage => stage <= now && stage > latestCutoff)
                            .DefaultIfEmpty(DateTime.MinValue)
                            .Max();
                        if (latestDueStage == DateTime.MinValue || now >= EnsureUtc(fixture.KickoffUtc))
                            return;

                        await intelligenceService.RunAsync(
                            new RunMatchIntelligenceCommand(fixture.FixtureId, now),
                            token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "Pre-match intelligence fixture failed. FixtureId={FixtureId}, Match={HomeTeam} vs {AwayTeam}",
                            fixture.FixtureId,
                            fixture.HomeTeam,
                            fixture.AwayTeam);
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Pre-match intelligence worker cycle failed");
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
