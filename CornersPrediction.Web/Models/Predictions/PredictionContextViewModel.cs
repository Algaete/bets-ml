using CornersPrediction.Web.Models.MatchHistory;

namespace CornersPrediction.Web.Models.Predictions;

public sealed record TeamRecentStatsViewModel(
    string TeamName,
    string Context,
    int MatchCount,
    double AvgCornersFor,
    double AvgCornersAgainst,
    double AvgShots,
    double AvgShotsAgainst,
    double AvgShotsOnGoal,
    double AvgShotsOnGoalAgainst,
    double AvgPossession,
    double AvgGoalsFor,
    double AvgGoalsAgainst);

public sealed record MatchHistorySummaryViewModel(
    TeamRecentStatsViewModel HomeGeneral,
    TeamRecentStatsViewModel HomeAsHome,
    TeamRecentStatsViewModel AwayGeneral,
    TeamRecentStatsViewModel AwayAsAway);

public sealed record PredictionComparisonViewModel(
    double GeneralWeight,
    double ConditionWeight,
    double HomeExpectedCorners,
    double AwayExpectedCorners,
    double TotalExpectedCorners,
    double HomeAttackVsAwayDefense,
    double AwayAttackVsHomeDefense,
    double EnrichedPrediction,
    double HomeExpectedShots,
    double AwayExpectedShots,
    double TotalExpectedShots,
    double HomeShotsAttackVsAwayDefense,
    double AwayShotsAttackVsHomeDefense,
    double EnrichedShotsPrediction,
    double HomeExpectedShotsOnGoal,
    double AwayExpectedShotsOnGoal,
    double TotalExpectedShotsOnGoal,
    double HomeShotsOnGoalAttackVsAwayDefense,
    double AwayShotsOnGoalAttackVsHomeDefense,
    double EnrichedShotsOnGoalPrediction,
    double? BaseLocalAwayPrediction,
    double? Difference,
    string Recommendation);

public sealed record PredictionContextViewModel(
    MatchHistorySummaryViewModel Summary,
    PredictionComparisonViewModel Comparison,
    IReadOnlyList<MatchHistoryItemViewModel> HomeGeneralMatches,
    IReadOnlyList<MatchHistoryItemViewModel> HomeAsHomeMatches,
    IReadOnlyList<MatchHistoryItemViewModel> AwayGeneralMatches,
    IReadOnlyList<MatchHistoryItemViewModel> AwayAsAwayMatches);
