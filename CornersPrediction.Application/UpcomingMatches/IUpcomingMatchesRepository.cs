namespace CornersPrediction.Application.UpcomingMatches;

public interface IUpcomingMatchesRepository
{
    Task<IReadOnlyList<UpcomingMatchDto>> GetNextWeekMatchesAsync(
        string? genero,
        string? liga,
        CancellationToken cancellationToken);
}
