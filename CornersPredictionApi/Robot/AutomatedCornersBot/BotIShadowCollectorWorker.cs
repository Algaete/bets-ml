using CornersPrediction.Application.Automation.BotI;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class BotIShadowCollectorOptions
{
    public const string SectionName = "BotIShadowCollector";
    public bool Enabled { get; init; } = true;
    public int StartupDelaySeconds { get; init; } = 90;
    public int PollMinutes { get; init; } = 15;
    public int FixtureLookAheadDays { get; init; } = 7;
    public int MaximumFixtures { get; init; } = 50;
}

/// <summary>
/// Independent, best-effort shadow collector. It runs outside the productive bot
/// runner, appends idempotent audits and has no dependency capable of publishing a
/// pick. A failed collection is logged and retried on the next interval.
/// </summary>
public sealed class BotIShadowCollectorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotIShadowCollectorOptions _options;
    private readonly ILogger<BotIShadowCollectorWorker> _logger;

    public BotIShadowCollectorWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BotIShadowCollectorOptions> options,
        ILogger<BotIShadowCollectorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Bot I2026 automatic shadow collection is disabled by configuration.");
            return;
        }

        if (_options.StartupDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(_options.StartupDelaySeconds), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.PollMinutes));
        do
        {
            await CollectOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<SqlAutomationRepository>()
                .EnsureSchemaAsync(cancellationToken);
            var collector = scope.ServiceProvider.GetRequiredService<IBotIShadowCollectorService>();
            var asOfUtc = DateTime.UtcNow;
            var localDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, SantiagoTimeZone));
            var result = await collector.CollectAsync(new BotICollectCommand(
                localDate,
                localDate.AddDays(_options.FixtureLookAheadDays + 1),
                asOfUtc,
                _options.MaximumFixtures), cancellationToken);
            _logger.LogInformation(
                "I2026 shadow collection completed. Timelines={Timelines}, Inserted={Inserted}, Existing={Existing}, Approved={Approved}, Rejected={Rejected}, Abstained={Abstained}",
                result.TimelinesLoaded,
                result.Inserted,
                result.AlreadyCaptured,
                result.Approved,
                result.Rejected,
                result.Abstained);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "I2026 shadow collection failed; productive bot execution was not affected.");
        }
    }

    private static readonly TimeZoneInfo SantiagoTimeZone = ResolveSantiagoTimeZone();

    private static TimeZoneInfo ResolveSantiagoTimeZone()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
