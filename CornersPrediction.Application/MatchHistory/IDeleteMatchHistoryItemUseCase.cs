namespace CornersPrediction.Application.MatchHistory;

public interface IDeleteMatchHistoryItemUseCase
{
    Task<int> DeleteAsync(int id, CancellationToken cancellationToken);
}
