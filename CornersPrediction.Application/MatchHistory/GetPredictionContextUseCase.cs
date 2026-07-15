using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Application.Teams;
using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.MatchHistory;

public sealed class GetPredictionContextUseCase : IGetPredictionContextUseCase
{
    private const double GeneralWeight = 0.60;
    private const double ConditionWeight = 0.40;
    private const string HomeQueryTeamCondition = "HOME";
    private const string AwayQueryTeamCondition = "AWAY";
    private const string GeneralHistoryType = "ULTIMOS_10_GENERAL";
    private const string LocalHistoryType = "ULTIMOS_10_LOCAL";
    private const string AwayHistoryType = "ULTIMOS_10_VISITA";

    private readonly IMatchHistoryRepository _repository;

    public GetPredictionContextUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<PredictionContextDto> GetAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string? teamGender,
        double? baseLocalAwayPrediction,
        CancellationToken cancellationToken)
    {
        Validate(homeTeam, awayTeam);

        var trimmedHomeTeam = homeTeam.Trim();
        var trimmedAwayTeam = awayTeam.Trim();
        var normalizedLeague = string.IsNullOrWhiteSpace(league) ? null : league.Trim();
        var normalizedTeamGender = TeamGenderOptions.Normalize(teamGender);

        var recentMatches = (await _repository.GetRecentAsync(
            trimmedHomeTeam,
            trimmedAwayTeam,
            normalizedLeague,
            normalizedTeamGender,
            cancellationToken)).ToArray();

        var homeGeneralMatches = TakeHistoryBucket(
            recentMatches,
            HomeQueryTeamCondition,
            GeneralHistoryType,
            () => TakeRecentForTeam(recentMatches, trimmedHomeTeam));
        var homeAsHomeMatches = TakeHistoryBucket(
            recentMatches,
            HomeQueryTeamCondition,
            LocalHistoryType,
            () => TakeRecentForTeamCondition(recentMatches, trimmedHomeTeam, mustBeHome: true));
        var awayGeneralMatches = TakeHistoryBucket(
            recentMatches,
            AwayQueryTeamCondition,
            GeneralHistoryType,
            () => TakeRecentForTeam(recentMatches, trimmedAwayTeam));
        var awayAsAwayMatches = TakeHistoryBucket(
            recentMatches,
            AwayQueryTeamCondition,
            AwayHistoryType,
            () => TakeRecentForTeamCondition(recentMatches, trimmedAwayTeam, mustBeHome: false));

        var homeGeneralStats = CalculateRecentStats(trimmedHomeTeam, "General", homeGeneralMatches);
        var homeAsHomeStats = CalculateRecentStats(trimmedHomeTeam, "Home", homeAsHomeMatches);
        var awayGeneralStats = CalculateRecentStats(trimmedAwayTeam, "General", awayGeneralMatches);
        var awayAsAwayStats = CalculateRecentStats(trimmedAwayTeam, "Away", awayAsAwayMatches);

        var summary = new MatchHistorySummaryDto(
            homeGeneralStats,
            homeAsHomeStats,
            awayGeneralStats,
            awayAsAwayStats);

        var comparison = BuildPredictionComparison(
            summary,
            baseLocalAwayPrediction);

        return new PredictionContextDto(
            summary,
            comparison,
            homeGeneralMatches.Select(MatchHistoryMapper.ToDto).ToArray(),
            homeAsHomeMatches.Select(MatchHistoryMapper.ToDto).ToArray(),
            awayGeneralMatches.Select(MatchHistoryMapper.ToDto).ToArray(),
            awayAsAwayMatches.Select(MatchHistoryMapper.ToDto).ToArray());
    }

    public static TeamRecentStatsDto CalculateRecentStats(
        string teamName,
        string context,
        IReadOnlyList<MatchHistoryItem> matches)
    {
        if (matches.Count == 0)
        {
            return new TeamRecentStatsDto(teamName, context, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        return new TeamRecentStatsDto(
            teamName,
            context,
            matches.Count,
            Average(matches, match => IsTeamHome(match, teamName) ? match.HomeCorners : match.AwayCorners),
            Average(matches, match => IsTeamHome(match, teamName) ? match.AwayCorners : match.HomeCorners),
            Average(matches, match => IsTeamHome(match, teamName) ? match.HomeShots : match.AwayShots),
            Average(matches, match => IsTeamHome(match, teamName) ? match.AwayShots : match.HomeShots),
            Average(matches, match => IsTeamHome(match, teamName) ? match.HomeShotsOnGoal : match.AwayShotsOnGoal),
            Average(matches, match => IsTeamHome(match, teamName) ? match.AwayShotsOnGoal : match.HomeShotsOnGoal),
            Average(matches, match => IsTeamHome(match, teamName) ? match.HomePossession : match.AwayPossession),
            Average(matches, match => IsTeamHome(match, teamName) ? match.HomeGoals : match.AwayGoals),
            Average(matches, match => IsTeamHome(match, teamName) ? match.AwayGoals : match.HomeGoals));
    }

    private static PredictionComparisonDto BuildPredictionComparison(
        MatchHistorySummaryDto summary,
        double? baseLocalAwayPrediction)
    {
        var homeExpectedCorners =
            summary.HomeGeneral.AvgCornersFor * GeneralWeight +
            summary.HomeAsHome.AvgCornersFor * ConditionWeight;

        var awayExpectedCorners =
            summary.AwayGeneral.AvgCornersFor * GeneralWeight +
            summary.AwayAsAway.AvgCornersFor * ConditionWeight;

        var totalExpectedCorners = homeExpectedCorners + awayExpectedCorners;

        var homeAttackVsAwayDefense = (
            homeExpectedCorners +
            summary.AwayGeneral.AvgCornersAgainst +
            summary.AwayAsAway.AvgCornersAgainst) / 3;

        var awayAttackVsHomeDefense = (
            awayExpectedCorners +
            summary.HomeGeneral.AvgCornersAgainst +
            summary.HomeAsHome.AvgCornersAgainst) / 3;

        var enrichedPrediction = homeAttackVsAwayDefense + awayAttackVsHomeDefense;
        var homeExpectedShots =
            summary.HomeGeneral.AvgShots * GeneralWeight +
            summary.HomeAsHome.AvgShots * ConditionWeight;

        var awayExpectedShots =
            summary.AwayGeneral.AvgShots * GeneralWeight +
            summary.AwayAsAway.AvgShots * ConditionWeight;

        var totalExpectedShots = homeExpectedShots + awayExpectedShots;

        var homeShotsAttackVsAwayDefense = (
            homeExpectedShots +
            summary.AwayGeneral.AvgShotsAgainst +
            summary.AwayAsAway.AvgShotsAgainst) / 3;

        var awayShotsAttackVsHomeDefense = (
            awayExpectedShots +
            summary.HomeGeneral.AvgShotsAgainst +
            summary.HomeAsHome.AvgShotsAgainst) / 3;

        var enrichedShotsPrediction = homeShotsAttackVsAwayDefense + awayShotsAttackVsHomeDefense;

        var homeExpectedShotsOnGoal =
            summary.HomeGeneral.AvgShotsOnGoal * GeneralWeight +
            summary.HomeAsHome.AvgShotsOnGoal * ConditionWeight;

        var awayExpectedShotsOnGoal =
            summary.AwayGeneral.AvgShotsOnGoal * GeneralWeight +
            summary.AwayAsAway.AvgShotsOnGoal * ConditionWeight;

        var totalExpectedShotsOnGoal = homeExpectedShotsOnGoal + awayExpectedShotsOnGoal;

        var homeShotsOnGoalAttackVsAwayDefense = (
            homeExpectedShotsOnGoal +
            summary.AwayGeneral.AvgShotsOnGoalAgainst +
            summary.AwayAsAway.AvgShotsOnGoalAgainst) / 3;

        var awayShotsOnGoalAttackVsHomeDefense = (
            awayExpectedShotsOnGoal +
            summary.HomeGeneral.AvgShotsOnGoalAgainst +
            summary.HomeAsHome.AvgShotsOnGoalAgainst) / 3;

        var enrichedShotsOnGoalPrediction = homeShotsOnGoalAttackVsAwayDefense + awayShotsOnGoalAttackVsHomeDefense;

        var homeExpectedGoals =
            summary.HomeGeneral.AvgGoalsFor * GeneralWeight +
            summary.HomeAsHome.AvgGoalsFor * ConditionWeight;

        var awayExpectedGoals =
            summary.AwayGeneral.AvgGoalsFor * GeneralWeight +
            summary.AwayAsAway.AvgGoalsFor * ConditionWeight;

        var totalExpectedGoals = homeExpectedGoals + awayExpectedGoals;

        var homeGoalsAttackVsAwayDefense = (
            homeExpectedGoals +
            summary.AwayGeneral.AvgGoalsAgainst +
            summary.AwayAsAway.AvgGoalsAgainst) / 3;

        var awayGoalsAttackVsHomeDefense = (
            awayExpectedGoals +
            summary.HomeGeneral.AvgGoalsAgainst +
            summary.HomeAsHome.AvgGoalsAgainst) / 3;

        var enrichedGoalsPrediction = homeGoalsAttackVsAwayDefense + awayGoalsAttackVsHomeDefense;

        double? difference = baseLocalAwayPrediction is null
            ? null
            : Math.Abs(enrichedPrediction - baseLocalAwayPrediction.Value);

        var recommendation = difference is null
            ? "Pending base prediction"
            : difference <= 1.0
                ? "Prediccion consistente"
                : "Revisar manualmente: los contextos difieren";

        return new PredictionComparisonDto(
            GeneralWeight,
            ConditionWeight,
            Round(homeExpectedCorners),
            Round(awayExpectedCorners),
            Round(totalExpectedCorners),
            Round(homeAttackVsAwayDefense),
            Round(awayAttackVsHomeDefense),
            Round(enrichedPrediction),
            Round(homeExpectedShots),
            Round(awayExpectedShots),
            Round(totalExpectedShots),
            Round(homeShotsAttackVsAwayDefense),
            Round(awayShotsAttackVsHomeDefense),
            Round(enrichedShotsPrediction),
            Round(homeExpectedShotsOnGoal),
            Round(awayExpectedShotsOnGoal),
            Round(totalExpectedShotsOnGoal),
            Round(homeShotsOnGoalAttackVsAwayDefense),
            Round(awayShotsOnGoalAttackVsHomeDefense),
            Round(enrichedShotsOnGoalPrediction),
            Round(homeExpectedGoals),
            Round(awayExpectedGoals),
            Round(totalExpectedGoals),
            Round(homeGoalsAttackVsAwayDefense),
            Round(awayGoalsAttackVsHomeDefense),
            Round(enrichedGoalsPrediction),
            baseLocalAwayPrediction is null ? null : Round(baseLocalAwayPrediction.Value),
            difference is null ? null : Round(difference.Value),
            recommendation);
    }

    private static bool IsTeamHome(MatchHistoryItem match, string teamName)
    {
        if (TeamNameEquals(match.HomeTeam, teamName))
        {
            return true;
        }

        if (TeamNameEquals(match.AwayTeam, teamName))
        {
            return false;
        }

        if (match.TeamCondition?.Equals("HOME", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (match.TeamCondition?.Equals("AWAY", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return match.HomeTeam.Equals(teamName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MatchHistoryItem> TakeRecentForTeam(
        IEnumerable<MatchHistoryItem> matches,
        string teamName)
    {
        return matches
            .Where(match => MatchIncludesTeam(match, teamName))
            .GroupBy(match => match.Id)
            .Select(group => group.First())
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();
    }

    private static IReadOnlyList<MatchHistoryItem> TakeHistoryBucket(
        IEnumerable<MatchHistoryItem> matches,
        string queryTeamCondition,
        string historyType,
        Func<IReadOnlyList<MatchHistoryItem>> fallback)
    {
        var bucketMatches = matches
            .Where(match => MatchTextEquals(match.QueryTeamCondition, queryTeamCondition))
            .Where(match => MatchTextEquals(match.HistoryType, historyType))
            .GroupBy(match => match.Id)
            .Select(group => group
                .OrderBy(match => match.HistoryRank ?? int.MaxValue)
                .First())
            .OrderBy(match => match.HistoryRank ?? int.MaxValue)
            .ThenByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();

        return bucketMatches.Length > 0 ? bucketMatches : fallback();
    }

    private static IReadOnlyList<MatchHistoryItem> TakeRecentForTeamCondition(
        IEnumerable<MatchHistoryItem> matches,
        string teamName,
        bool mustBeHome)
    {
        return matches
            .Where(match => MatchIncludesTeam(match, teamName))
            .GroupBy(match => match.Id)
            .Select(group => group.First())
            .Where(match => IsTeamHome(match, teamName) == mustBeHome)
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();
    }

    private static bool MatchIncludesTeam(MatchHistoryItem match, string teamName)
    {
        return TeamNameEquals(match.HomeTeam, teamName) ||
            TeamNameEquals(match.AwayTeam, teamName);
    }

    private static bool TeamNameEquals(string left, string right)
    {
        return left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchTextEquals(string? left, string right)
    {
        return left?.Trim().Equals(right, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static double Average(
        IReadOnlyList<MatchHistoryItem> matches,
        Func<MatchHistoryItem, double> selector)
    {
        return Round(matches.Average(selector));
    }

    private static double Round(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static void Validate(string homeTeam, string awayTeam)
    {
        if (string.IsNullOrWhiteSpace(homeTeam))
        {
            throw new ArgumentException("Home team is required.");
        }

        if (string.IsNullOrWhiteSpace(awayTeam))
        {
            throw new ArgumentException("Away team is required.");
        }

        if (homeTeam.Equals(awayTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home team and away team must be different.");
        }
    }
}
