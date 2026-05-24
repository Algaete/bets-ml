using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.Abstractions.Persistence;

/// <summary>
/// Persistence port for historical match records.
/// </summary>
public interface IMatchHistoryRepository
{
    Task<MatchHistoryItem> AddAsync(MatchHistoryItem item, CancellationToken cancellationToken);

    Task<int> UpdateAsync(int id, MatchHistoryItem item, CancellationToken cancellationToken);

    Task<int> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetRecentAsync(
        string homeTeam,
        string awayTeam,
        CancellationToken cancellationToken);
}
