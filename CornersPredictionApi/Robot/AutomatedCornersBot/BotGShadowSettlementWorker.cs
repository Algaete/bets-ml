using CornersPrediction.Application.Automation.BotG;
using Microsoft.Extensions.Options;

namespace AutomatedCornersBot.Api;

public sealed class BotGShadowSettlementOptions
{
    public const string SectionName = "BotGShadowSettlement";

    public bool Enabled { get; set; } = true;
    public int StartupDelaySeconds { get; set; } = 90;
    public int PollMinutes { get; set; } = 15;
    public int MaximumCandidates { get; set; } = 20_000;
}

/// <summary>
/// Reconciles shadow outcomes independently from the long-running API-Football import.
/// It only consumes already verified MatchHistory rows through the fail-closed G settlement SP.
/// </summary>
public sealed class BotGShadowSettlementWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SqlAutomationRepository _schemaRepository;
    private readonly BotGShadowSettlementOptions _options;
    private readonly ILogger<BotGShadowSettlementWorker> _logger;

    public BotGShadowSettlementWorker(
        IServiceScopeFactory scopeFactory,
        SqlAutomationRepository schemaRepository,
        IOptions<BotGShadowSettlementOptions> options,
        ILogger<BotGShadowSettlementWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _schemaRepository = schemaRepository;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Bot G shadow settlement worker is disabled.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(_options.StartupDelaySeconds), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.PollMinutes));

        do
        {
            await ReconcileAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _schemaRepository.EnsureSchemaAsync(cancellationToken);
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBotGCandidateReadRepository>();
            var result = await repository.SettlePendingAsync(
                new SettleBotG2026CandidatesCommand(
                    DateTime.UtcNow,
                    _options.MaximumCandidates,
                    DryRun: false),
                cancellationToken);

            if (result.SettledCandidates > 0 || result.EligibleCandidates > 0)
            {
                _logger.LogInformation(
                    "Bot G shadow settlement completed. Scanned={Scanned}, Eligible={Eligible}, Settled={Settled}, Pending={Pending}",
                    result.ScannedCandidates,
                    result.EligibleCandidates,
                    result.SettledCandidates,
                    result.RemainingPendingCandidates);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Bot G shadow settlement failed; the worker will retry without delaying API-Football imports");
        }
    }
}
