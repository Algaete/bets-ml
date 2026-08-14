namespace CornersPrediction.Application.MatchHistory;

public interface IGetPredictionContextUseCase
{
    Task<PredictionContextDto> GetAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string? teamGender,
        double? baseLocalAwayPrediction,
        DateOnly? beforeDate,
        CancellationToken cancellationToken);
}
