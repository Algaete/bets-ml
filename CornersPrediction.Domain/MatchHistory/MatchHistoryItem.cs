namespace CornersPrediction.Domain.MatchHistory;

/// <summary>
/// Raw historical match data used later to calculate model features such as last 3/5/10 averages.
/// </summary>
public sealed class MatchHistoryItem
{
    public int Id { get; set; }
    public string? TeamCondition { get; set; }
    public string? QueryTeamCondition { get; set; }
    public string? HistoryType { get; set; }
    public int? HistoryRank { get; set; }
    public required string League { get; set; }
    public required string Season { get; set; }
    public DateOnly MatchDate { get; set; }
    public bool IsKnockout { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public string? HomeFormation { get; set; }
    public string? AwayFormation { get; set; }
    public int HomeCorners { get; set; }
    public int AwayCorners { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int HomeShots { get; set; }
    public int AwayShots { get; set; }
    public int HomeShotsOnGoal { get; set; }
    public int AwayShotsOnGoal { get; set; }
    public double HomePossession { get; set; }
    public double AwayPossession { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int TotalCorners => HomeCorners + AwayCorners;
}
