namespace CornersPrediction.Web.Models.MatchHistory;

using CornersPrediction.Web.Models.Teams;

public sealed class MatchHistoryIndexViewModel
{
    public CreateMatchHistoryViewModel Form { get; init; } = new();

    public MatchHistoryFiltersViewModel Filters { get; init; } = new();

    public IReadOnlyList<MatchHistoryItemViewModel> Records { get; init; } =
        Array.Empty<MatchHistoryItemViewModel>();

    public IReadOnlyList<MatchHistoryItemViewModel> RecentMatches { get; init; } =
        Array.Empty<MatchHistoryItemViewModel>();

    public IReadOnlyList<string> LeagueOptions { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<string> FormationOptions { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<TeamBi3InfoViewModel> TeamOptions { get; init; } =
        Array.Empty<TeamBi3InfoViewModel>();
}
