namespace CornersPrediction.Application.Automation;

public interface ICornersPipelineService
{
    Task<CornersPipelineStepResult> RunMatchHistoryAsync(int days, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunWorldCupMatchHistoryAsync(int days, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunUpcomingMatchesAsync(int days, CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunPinnacleOddsAsync(CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunBetanoOddsAsync(CancellationToken cancellationToken);

    Task<CornersPipelineStepResult> RunBotsAsync(bool excludeExistingSelections, CancellationToken cancellationToken);

    Task<CornersPipelineRunResult> RunFullPipelineAsync(
        RunFullPipelineCommand command,
        CancellationToken cancellationToken);
}
