namespace CornersPrediction.Web.Models.Predictions;

public sealed class PredictionIndexViewModel
{
    public IReadOnlyList<string> LeagueOptions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FormationOptions { get; init; } = Array.Empty<string>();
}
