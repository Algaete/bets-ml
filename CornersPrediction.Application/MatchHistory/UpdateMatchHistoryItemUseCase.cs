using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.MatchHistory;

/// <summary>
/// Updates historical match records when captured stats need correction.
/// </summary>
public sealed class UpdateMatchHistoryItemUseCase : IUpdateMatchHistoryItemUseCase
{
    private readonly IMatchHistoryRepository _repository;

    public UpdateMatchHistoryItemUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> UpdateAsync(
        int id,
        UpdateMatchHistoryItemCommand command,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Match history id must be greater than zero.");
        }

        Validate(command);

        var item = new MatchHistoryItem
        {
            Id = id,
            League = command.League.Trim(),
            Season = command.Season.Trim(),
            MatchDate = command.MatchDate,
            IsKnockout = command.IsKnockout,
            HomeTeam = command.HomeTeam.Trim(),
            AwayTeam = command.AwayTeam.Trim(),
            HomeFormation = NormalizeOptional(command.HomeFormation),
            AwayFormation = NormalizeOptional(command.AwayFormation),
            HomeCorners = command.HomeCorners,
            AwayCorners = command.AwayCorners,
            HomeGoals = command.HomeGoals,
            AwayGoals = command.AwayGoals,
            HomeShots = command.HomeShots,
            AwayShots = command.AwayShots,
            HomeShotsOnGoal = command.HomeShotsOnGoal,
            AwayShotsOnGoal = command.AwayShotsOnGoal,
            HomePossession = command.HomePossession,
            AwayPossession = command.AwayPossession
        };

        return await _repository.UpdateAsync(id, item, cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void Validate(UpdateMatchHistoryItemCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.League))
        {
            throw new ArgumentException("League is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Season))
        {
            throw new ArgumentException("Season is required.");
        }

        if (string.IsNullOrWhiteSpace(command.HomeTeam))
        {
            throw new ArgumentException("Home team is required.");
        }

        if (string.IsNullOrWhiteSpace(command.AwayTeam))
        {
            throw new ArgumentException("Away team is required.");
        }

        if (command.HomeTeam.Equals(command.AwayTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home team and away team must be different.");
        }

        if (command.HomePossession is < 0 or > 100 || command.AwayPossession is < 0 or > 100)
        {
            throw new ArgumentException("Possession values must be between 0 and 100.");
        }
    }
}
