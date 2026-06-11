namespace CornersPrediction.Application.MatchHistory;

public interface IGetManualMatchHistoryEntriesUseCase
{
    Task<IReadOnlyList<MatchHistoryItemDto>> GetAsync(
        string? league,
        string? team,
        int take,
        CancellationToken cancellationToken);
}
