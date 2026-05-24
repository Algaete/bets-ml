using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.MatchHistory;

internal static class MatchHistoryMapper
{
    public static MatchHistoryItemDto ToDto(MatchHistoryItem item)
    {
        return new MatchHistoryItemDto(
            item.Id,
            item.TeamCondition,
            item.League,
            item.Season,
            item.MatchDate,
            item.IsKnockout,
            item.HomeTeam,
            item.AwayTeam,
            item.HomeFormation,
            item.AwayFormation,
            item.HomeCorners,
            item.AwayCorners,
            item.HomeGoals,
            item.AwayGoals,
            item.HomeShots,
            item.AwayShots,
            item.HomeShotsOnGoal,
            item.AwayShotsOnGoal,
            item.HomePossession,
            item.AwayPossession,
            item.TotalCorners,
            item.CreatedAtUtc);
    }
}
