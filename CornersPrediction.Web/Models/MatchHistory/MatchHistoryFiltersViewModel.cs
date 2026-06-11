namespace CornersPrediction.Web.Models.MatchHistory;

public sealed class MatchHistoryFiltersViewModel
{
    public string? League { get; set; }
    public string? Team { get; set; }
    public int Take { get; set; } = 20;
}
