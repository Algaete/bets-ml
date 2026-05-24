using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.MatchHistory;

/// <summary>
/// Creates historical match records that will later feed average-calculation workflows.
/// </summary>
public sealed class CreateMatchHistoryItemUseCase : ICreateMatchHistoryItemUseCase
{
    private readonly IMatchHistoryRepository _repository;

    public CreateMatchHistoryItemUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<MatchHistoryItemDto> CreateAsync(
        CreateMatchHistoryItemCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var item = new MatchHistoryItem
        {
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

        var saved = await _repository.AddAsync(item, cancellationToken);
        return MatchHistoryMapper.ToDto(saved);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void Validate(CreateMatchHistoryItemCommand command)
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
