using CornersPrediction.Application.Abstractions.Persistence;

namespace CornersPrediction.Application.MatchHistory;

/// <summary>
/// Deletes historical match records that should not feed future feature calculations.
/// </summary>
public sealed class DeleteMatchHistoryItemUseCase : IDeleteMatchHistoryItemUseCase
{
    private readonly IMatchHistoryRepository _repository;

    public DeleteMatchHistoryItemUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Match history id must be greater than zero.");
        }

        return await _repository.DeleteAsync(id, cancellationToken);
    }
}
