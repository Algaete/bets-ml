using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Teams;

namespace CornersPrediction.Application.MatchHistory;

/// <summary>
/// Retrieves the most recent home/away history for the selected teams.
/// </summary>
public sealed class GetRecentMatchHistoryUseCase : IGetRecentMatchHistoryUseCase
{
    private readonly IMatchHistoryRepository _repository;

    public GetRecentMatchHistoryUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MatchHistoryItemDto>> GetAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        Validate(homeTeam, awayTeam);
        var normalizedTeamGender = TeamGenderOptions.Normalize(teamGender);

        var items = await _repository.GetRecentAsync(
            homeTeam.Trim(),
            awayTeam.Trim(),
            string.IsNullOrWhiteSpace(league) ? null : league.Trim(),
            normalizedTeamGender,
            cancellationToken);

        return items.Select(MatchHistoryMapper.ToDto).ToArray();
    }

    private static void Validate(string homeTeam, string awayTeam)
    {
        if (string.IsNullOrWhiteSpace(homeTeam))
        {
            throw new ArgumentException("Home team is required.");
        }

        if (string.IsNullOrWhiteSpace(awayTeam))
        {
            throw new ArgumentException("Away team is required.");
        }

        if (homeTeam.Equals(awayTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home team and away team must be different.");
        }
    }
}
