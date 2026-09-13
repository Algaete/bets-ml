namespace CornersPrediction.Application.AutomatedCorners;

/// <summary>
/// Reads only the selection fields consumed by performance calculations.
/// The projection stays on covering scalar columns. Scorecards use the stored
/// implied probability fallback, while scientific evaluations provide their
/// bookmaker-neutral market probability separately.
/// </summary>
public interface IAutomatedBotPerformanceSelectionsRepository
{
    Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetPerformanceSelectionsAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);
}
