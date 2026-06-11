namespace CornersPrediction.Application.UpcomingMatches;

public sealed class GetUpcomingMatchesUseCase : IGetUpcomingMatchesUseCase
{
    private readonly IUpcomingMatchesRepository _repository;

    public GetUpcomingMatchesUseCase(IUpcomingMatchesRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<UpcomingMatchDto>> GetAsync(
        string? genero,
        string? liga,
        CancellationToken cancellationToken)
    {
        return _repository.GetNextWeekMatchesAsync(
            NormalizeFilter(genero),
            NormalizeFilter(liga),
            cancellationToken);
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
