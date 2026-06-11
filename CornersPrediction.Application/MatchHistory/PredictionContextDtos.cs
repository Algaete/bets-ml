namespace CornersPrediction.Application.MatchHistory;

public sealed record TeamRecentStatsDto(
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

public sealed record MatchHistorySummaryDto(
    TeamRecentStatsDto HomeGeneral,
    TeamRecentStatsDto HomeAsHome,
    TeamRecentStatsDto AwayGeneral,
    TeamRecentStatsDto AwayAsAway);

public sealed record PredictionComparisonDto(
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

public sealed record PredictionContextDto(
    MatchHistorySummaryDto Summary,
    PredictionComparisonDto Comparison,
    IReadOnlyList<MatchHistoryItemDto> HomeGeneralMatches,
    IReadOnlyList<MatchHistoryItemDto> HomeAsHomeMatches,
    IReadOnlyList<MatchHistoryItemDto> AwayGeneralMatches,
    IReadOnlyList<MatchHistoryItemDto> AwayAsAwayMatches);
