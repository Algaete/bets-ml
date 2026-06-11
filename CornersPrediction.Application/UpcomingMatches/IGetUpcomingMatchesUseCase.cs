namespace CornersPrediction.Application.UpcomingMatches;

public interface IGetUpcomingMatchesUseCase
{
    Task<IReadOnlyList<UpcomingMatchDto>> GetAsync(
        string? genero,
        string? liga,
        CancellationToken cancellationToken);
}
