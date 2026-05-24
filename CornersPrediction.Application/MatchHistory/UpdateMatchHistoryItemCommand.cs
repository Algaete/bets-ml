namespace CornersPrediction.Application.MatchHistory;

public sealed record UpdateMatchHistoryItemCommand(
    string League,
    string Season,
    DateOnly MatchDate,
    bool IsKnockout,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    int HomeCorners,
    int AwayCorners,
    int HomeGoals,
    int AwayGoals,
    int HomeShots,
    int AwayShots,
    int HomeShotsOnGoal,
    int AwayShotsOnGoal,
    double HomePossession,
    double AwayPossession);
