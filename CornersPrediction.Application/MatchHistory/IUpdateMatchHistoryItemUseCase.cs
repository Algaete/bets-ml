namespace CornersPrediction.Application.MatchHistory;

public interface IUpdateMatchHistoryItemUseCase
{
    Task<int> UpdateAsync(
        int id,
        UpdateMatchHistoryItemCommand command,
        CancellationToken cancellationToken);
}
