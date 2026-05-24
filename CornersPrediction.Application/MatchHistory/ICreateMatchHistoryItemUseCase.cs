namespace CornersPrediction.Application.MatchHistory;

public interface ICreateMatchHistoryItemUseCase
{
    Task<MatchHistoryItemDto> CreateAsync(CreateMatchHistoryItemCommand command, CancellationToken cancellationToken);
}
