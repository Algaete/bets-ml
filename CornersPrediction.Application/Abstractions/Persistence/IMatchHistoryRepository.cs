using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.Abstractions.Persistence;

/// <summary>
/// Persistence port for historical match records.
/// </summary>
public interface IMatchHistoryRepository
{
    Task<MatchHistoryItem> AddAsync(MatchHistoryItem item, CancellationToken cancellationToken);

    Task<MatchHistoryBulkImportResult> BulkImportAsync(
        MatchHistoryBulkImportRequest request,
        CancellationToken cancellationToken);

    Task<int> UpdateAsync(int id, MatchHistoryItem item, CancellationToken cancellationToken);

    Task<int> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetRecentAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetManualEntriesAsync(
        string? league,
        string? team,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetLast10GeneralMatchesAsync(
        string team,
        string? league,
        string teamGender,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetLast10HomeMatchesAsync(
        string homeTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchHistoryItem>> GetLast10AwayMatchesAsync(
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken);
}
