namespace CornersPrediction.Application.MatchHistory;

public interface IGetRecentMatchHistoryUseCase
{
    Task<IReadOnlyList<MatchHistoryItemDto>> GetAsync(
        string homeTeam,
        string awayTeam,
        CancellationToken cancellationToken);
}
