namespace CornersPrediction.Application.Automation;

public interface ICornersPipelineService
{
    Task<CornersPipelineStepResult> RunMatchHistoryAsync(int days, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunUpcomingMatchesAsync(int days, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunPinnacleOddsAsync(CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunBetanoOddsAsync(CancellationToken cancellationToken);

    Task<BotOddsAvailability> GetBotOddsAvailabilityAsync(int batchSize, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunBotsAsync(RunBotsCommand command, CancellationToken cancellationToken);

    Task<CornersPipelineRunResult> RunFullPipelineAsync(
        RunFullPipelineCommand command,
        CancellationToken cancellationToken);
}
