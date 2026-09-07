namespace CornersPrediction.Application.AutomatedCorners;

/// <summary>
/// Reads only the selection fields consumed by performance calculations.
/// DecisionReason retains the original JSON types of botProfile and both
/// spellings of marketNoVigProbability; full feature snapshots stay in storage.
/// </summary>
public interface IAutomatedBotPerformanceSelectionsRepository
{
    Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetPerformanceSelectionsAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);
}
