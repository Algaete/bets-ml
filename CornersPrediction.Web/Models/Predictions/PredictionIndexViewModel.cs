namespace CornersPrediction.Web.Models.Predictions;

public sealed class PredictionIndexViewModel
{
    public IReadOnlyList<string> LeagueOptions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> FormationOptions { get; init; } = Array.Empty<string>();

    public string? League { get; init; }

    public string? Season { get; init; }

    public DateTime? MatchDate { get; init; }

    public string? HomeTeam { get; init; }

    public string? AwayTeam { get; init; }

    public string TeamGender { get; init; } = "M";

    public bool IsKnockout { get; init; }
}
