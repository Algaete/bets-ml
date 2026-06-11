using CornersPrediction.Application.Abstractions.Persistence;

namespace CornersPrediction.Application.MatchHistory;

public sealed class GetManualMatchHistoryEntriesUseCase : IGetManualMatchHistoryEntriesUseCase
{
    private readonly IMatchHistoryRepository _repository;

    public GetManualMatchHistoryEntriesUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MatchHistoryItemDto>> GetAsync(
        string? league,
        string? team,
        int take,
        CancellationToken cancellationToken)
    {
        var boundedTake = Math.Clamp(take, 1, 100);
        var entries = await _repository.GetManualEntriesAsync(
            string.IsNullOrWhiteSpace(league) ? null : league.Trim(),
            string.IsNullOrWhiteSpace(team) ? null : team.Trim(),
            boundedTake,
            cancellationToken);

        return entries.Select(MatchHistoryMapper.ToDto).ToArray();
    }
}
