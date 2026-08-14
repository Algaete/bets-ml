using System.Globalization;

namespace AutomatedCornersBot.Api;

public sealed class FeatureBuilder
{
    public Dictionary<string, object?> Build(
        UpcomingOddsRecord odds,
        PredictionContextDto context,
        IReadOnlyList<TeamBi3InfoDto> teamInfo)
    {
        var homeTeam = odds.EffectiveHomeTeam;
        var awayTeam = odds.EffectiveAwayTeam;
        var homeGeneralMatches = context.HomeGeneralMatches ?? Array.Empty<MatchHistoryItemDto>();
        var homeConditionMatches = context.HomeAsHomeMatches ?? Array.Empty<MatchHistoryItemDto>();
        var awayGeneralMatches = context.AwayGeneralMatches ?? Array.Empty<MatchHistoryItemDto>();
        var awayConditionMatches = context.AwayAsAwayMatches ?? Array.Empty<MatchHistoryItemDto>();

        var latestHomeFormation = FindLatestFormation(homeGeneralMatches, homeTeam);
        var latestAwayFormation = FindLatestFormation(awayGeneralMatches, awayTeam);
        var homeFormation = latestHomeFormation is { } homeFormationInfo && IsKnownFormation(homeFormationInfo.formation)
            ? homeFormationInfo.formation!.Trim()
            : "Unknown";
        var awayFormation = latestAwayFormation is { } awayFormationInfo && IsKnownFormation(awayFormationInfo.formation)
            ? awayFormationInfo.formation!.Trim()
            : "Unknown";
        var homeBig3 = GetBig3Flag(teamInfo, homeTeam);
        var awayBig3 = GetBig3Flag(teamInfo, awayTeam);

        var features = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["League"] = odds.EffectiveLeague,
            ["Season"] = DetermineSeason(odds),
            ["MatchDate"] = odds.MatchDate,
            ["HomeTeam"] = homeTeam,
            ["AwayTeam"] = awayTeam,
            ["HomeFormation"] = homeFormation,
            ["AwayFormation"] = awayFormation,
            ["HomeHasFormation"] = IsKnownFormation(homeFormation) ? 1 : 0,
            ["AwayHasFormation"] = IsKnownFormation(awayFormation) ? 1 : 0,
            ["big3home"] = homeBig3,
            ["big3away"] = awayBig3,
            ["Big3Diff"] = homeBig3 - awayBig3,
            ["IsKnockout"] = IsLikelyKnockoutCompetition(odds.EffectiveLeague) ? 1 : 0,
            ["BettingLine"] = Convert.ToDouble(odds.LineValue, CultureInfo.InvariantCulture),
            ["GoalsLine"] = odds.MarketType == "GoalsTotal"
                ? Convert.ToDouble(odds.LineValue, CultureInfo.InvariantCulture)
                : 2.5d,
            ["ShotsOnGoalLine"] = odds.MarketType == "ShotsOnTargetTotal"
                ? Convert.ToDouble(odds.LineValue, CultureInfo.InvariantCulture)
                : 8.5d,
            ["HomeIsCountry"] = 0,
            ["AwayIsCountry"] = 0,
            ["CountryDiff"] = 0
        };

        features["Home_MatchesLast3"] = Take(homeGeneralMatches, 3).Count;
        features["Away_MatchesLast3"] = Take(awayGeneralMatches, 3).Count;
        features["Home_MatchesLast5"] = Take(homeGeneralMatches, 5).Count;
        features["Away_MatchesLast5"] = Take(awayGeneralMatches, 5).Count;
        features["Home_HomeMatchesLast10"] = Take(homeConditionMatches, 10).Count;
        features["Away_AwayMatchesLast10"] = Take(awayConditionMatches, 10).Count;

        AddWindowFeatures(features, "Home", homeGeneralMatches, homeTeam, 3);
        AddWindowFeatures(features, "Away", awayGeneralMatches, awayTeam, 3);
        AddWindowFeatures(features, "Home", homeGeneralMatches, homeTeam, 5);
        AddWindowFeatures(features, "Away", awayGeneralMatches, awayTeam, 5);
        AddConditionFeatures(features, "Home", "Home", homeConditionMatches, homeTeam);
        AddConditionFeatures(features, "Away", "Away", awayConditionMatches, awayTeam);

        features["HomeCornersPowerLast5"] = Round((ToDouble(features["Home_AvgCornersForLast5"]) + ToDouble(features["Away_AvgCornersAgainstLast5"])) / 2);
        features["AwayCornersPowerLast5"] = Round((ToDouble(features["Away_AvgCornersForLast5"]) + ToDouble(features["Home_AvgCornersAgainstLast5"])) / 2);
        features["ExpectedTotalCornersPowerLast5"] = Round(ToDouble(features["HomeCornersPowerLast5"]) + ToDouble(features["AwayCornersPowerLast5"]));
        features["CornersDiffLast5"] = Round(ToDouble(features["Home_AvgCornersForLast5"]) - ToDouble(features["Away_AvgCornersForLast5"]));
        features["TotalStdCornersLast5"] = Round(ToDouble(features["Home_StdCornersForLast5"]) + ToDouble(features["Away_StdCornersForLast5"]));
        features["TotalRangeCornersLast5"] = Round(ToDouble(features["Home_RangeCornersForLast5"]) + ToDouble(features["Away_RangeCornersForLast5"]));
        features["HomeShotsPowerLast5"] = Round((ToDouble(features["Home_AvgShotsForLast5"]) + ToDouble(features["Away_AvgShotsAgainstLast5"])) / 2);
        features["AwayShotsPowerLast5"] = Round((ToDouble(features["Away_AvgShotsForLast5"]) + ToDouble(features["Home_AvgShotsAgainstLast5"])) / 2);
        features["ExpectedTotalShotsPowerLast5"] = Round(ToDouble(features["HomeShotsPowerLast5"]) + ToDouble(features["AwayShotsPowerLast5"]));
        features["ShotsDiffLast5"] = Round(ToDouble(features["Home_AvgShotsForLast5"]) - ToDouble(features["Away_AvgShotsForLast5"]));
        features["TotalStdShotsLast5"] = Round(ToDouble(features["Home_StdShotsForLast5"]) + ToDouble(features["Away_StdShotsForLast5"]));
        features["TotalRangeShotsLast5"] = Round(ToDouble(features["Home_RangeShotsForLast5"]) + ToDouble(features["Away_RangeShotsForLast5"]));
        features["HomeShotsOnGoalPowerLast5"] = Round((ToDouble(features["Home_AvgShotsOnGoalForLast5"]) + ToDouble(features["Away_AvgShotsOnGoalAgainstLast5"])) / 2);
        features["AwayShotsOnGoalPowerLast5"] = Round((ToDouble(features["Away_AvgShotsOnGoalForLast5"]) + ToDouble(features["Home_AvgShotsOnGoalAgainstLast5"])) / 2);
        features["ExpectedTotalShotsOnGoalPowerLast5"] = Round(ToDouble(features["HomeShotsOnGoalPowerLast5"]) + ToDouble(features["AwayShotsOnGoalPowerLast5"]));
        features["ShotsOnGoalDiffLast5"] = Round(ToDouble(features["Home_AvgShotsOnGoalForLast5"]) - ToDouble(features["Away_AvgShotsOnGoalForLast5"]));
        features["TotalStdShotsOnGoalLast5"] = Round(ToDouble(features["Home_StdShotsOnGoalForLast5"]) + ToDouble(features["Away_StdShotsOnGoalForLast5"]));
        features["TotalRangeShotsOnGoalLast5"] = Round(ToDouble(features["Home_RangeShotsOnGoalForLast5"]) + ToDouble(features["Away_RangeShotsOnGoalForLast5"]));
        features["HomeGoalsPowerLast5"] = Round((ToDouble(features["Home_AvgGoalsForLast5"]) + ToDouble(features["Away_AvgGoalsAgainstLast5"])) / 2);
        features["AwayGoalsPowerLast5"] = Round((ToDouble(features["Away_AvgGoalsForLast5"]) + ToDouble(features["Home_AvgGoalsAgainstLast5"])) / 2);
        features["ExpectedTotalGoalsPowerLast5"] = Round(ToDouble(features["HomeGoalsPowerLast5"]) + ToDouble(features["AwayGoalsPowerLast5"]));
        features["PossessionDiffLast5"] = Round(ToDouble(features["Home_AvgPossessionLast5"]) - ToDouble(features["Away_AvgPossessionLast5"]));
        features["GoalsForDiffLast5"] = Round(ToDouble(features["Home_AvgGoalsForLast5"]) - ToDouble(features["Away_AvgGoalsForLast5"]));
        features["GoalsAgainstDiffLast5"] = Round(ToDouble(features["Home_AvgGoalsAgainstLast5"]) - ToDouble(features["Away_AvgGoalsAgainstLast5"]));

        return features;
    }

    private static void AddWindowFeatures(
        IDictionary<string, object?> features,
        string prefix,
        IReadOnlyList<MatchHistoryItemDto> matches,
        string teamName,
        int count)
    {
        var window = Take(matches, count);
        var cornersFor = window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Corners)).ToArray();
        var cornersAgainst = window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.Corners)).ToArray();
        var shotsFor = window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Shots)).ToArray();
        var shotsAgainst = window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.Shots)).ToArray();
        var shotsOnGoalFor = window.Select(match => GetTeamMetric(match, teamName, TeamMetric.ShotsOnGoal)).ToArray();
        var shotsOnGoalAgainst = window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.ShotsOnGoal)).ToArray();

        features[$"{prefix}_AvgCornersForLast{count}"] = Average(cornersFor);
        features[$"{prefix}_AvgCornersAgainstLast{count}"] = Average(cornersAgainst);
        features[$"{prefix}_AvgTotalCornersLast{count}"] = Round(ToDouble(features[$"{prefix}_AvgCornersForLast{count}"]) + ToDouble(features[$"{prefix}_AvgCornersAgainstLast{count}"]));
        features[$"{prefix}_AvgShotsForLast{count}"] = Average(shotsFor);
        features[$"{prefix}_AvgShotsAgainstLast{count}"] = Average(shotsAgainst);
        features[$"{prefix}_AvgTotalShotsLast{count}"] = Round(ToDouble(features[$"{prefix}_AvgShotsForLast{count}"]) + ToDouble(features[$"{prefix}_AvgShotsAgainstLast{count}"]));
        features[$"{prefix}_AvgShotsOnGoalForLast{count}"] = Average(shotsOnGoalFor);
        features[$"{prefix}_AvgShotsOnGoalAgainstLast{count}"] = Average(shotsOnGoalAgainst);
        features[$"{prefix}_AvgTotalShotsOnGoalLast{count}"] = Round(ToDouble(features[$"{prefix}_AvgShotsOnGoalForLast{count}"]) + ToDouble(features[$"{prefix}_AvgShotsOnGoalAgainstLast{count}"]));
        features[$"{prefix}_AvgPossessionLast{count}"] = Average(window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Possession)).ToArray());
        features[$"{prefix}_AvgGoalsForLast{count}"] = Average(window.Select(match => GetTeamGoals(match, teamName)).ToArray());
        features[$"{prefix}_AvgGoalsAgainstLast{count}"] = Average(window.Select(match => GetOpponentGoals(match, teamName)).ToArray());

        if (count == 5)
        {
            features[$"{prefix}_StdCornersForLast5"] = StandardDeviation(cornersFor);
            features[$"{prefix}_RangeCornersForLast5"] = Range(cornersFor);
            features[$"{prefix}_StdShotsForLast5"] = StandardDeviation(shotsFor);
            features[$"{prefix}_RangeShotsForLast5"] = Range(shotsFor);
            features[$"{prefix}_StdShotsOnGoalForLast5"] = StandardDeviation(shotsOnGoalFor);
            features[$"{prefix}_RangeShotsOnGoalForLast5"] = Range(shotsOnGoalFor);
        }
    }

    private static void AddConditionFeatures(
        IDictionary<string, object?> features,
        string prefix,
        string contextName,
        IReadOnlyList<MatchHistoryItemDto> matches,
        string teamName)
    {
        var window = Take(matches, 10);
        features[$"{prefix}_{contextName}AvgCornersForLast10"] = Average(window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Corners)).ToArray());
        features[$"{prefix}_{contextName}AvgCornersAgainstLast10"] = Average(window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.Corners)).ToArray());
        features[$"{prefix}_{contextName}AvgTotalCornersLast10"] = Round(ToDouble(features[$"{prefix}_{contextName}AvgCornersForLast10"]) + ToDouble(features[$"{prefix}_{contextName}AvgCornersAgainstLast10"]));
        features[$"{prefix}_{contextName}AvgShotsForLast10"] = Average(window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Shots)).ToArray());
        features[$"{prefix}_{contextName}AvgShotsAgainstLast10"] = Average(window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.Shots)).ToArray());
        features[$"{prefix}_{contextName}AvgTotalShotsLast10"] = Round(ToDouble(features[$"{prefix}_{contextName}AvgShotsForLast10"]) + ToDouble(features[$"{prefix}_{contextName}AvgShotsAgainstLast10"]));
        features[$"{prefix}_{contextName}AvgShotsOnGoalForLast10"] = Average(window.Select(match => GetTeamMetric(match, teamName, TeamMetric.ShotsOnGoal)).ToArray());
        features[$"{prefix}_{contextName}AvgShotsOnGoalAgainstLast10"] = Average(window.Select(match => GetOpponentMetric(match, teamName, TeamMetric.ShotsOnGoal)).ToArray());
        features[$"{prefix}_{contextName}AvgTotalShotsOnGoalLast10"] = Round(ToDouble(features[$"{prefix}_{contextName}AvgShotsOnGoalForLast10"]) + ToDouble(features[$"{prefix}_{contextName}AvgShotsOnGoalAgainstLast10"]));
        features[$"{prefix}_{contextName}AvgPossessionLast10"] = Average(window.Select(match => GetTeamMetric(match, teamName, TeamMetric.Possession)).ToArray());
    }

    private static IReadOnlyList<MatchHistoryItemDto> Take(IReadOnlyList<MatchHistoryItemDto> items, int count) =>
        items.Take(Math.Min(count, items.Count)).ToArray();

    private static bool IsTeamHomeInMatch(MatchHistoryItemDto match, string teamName) =>
        NormalizeText(match.HomeTeam) == NormalizeText(teamName);

    private static bool IsTeamAwayInMatch(MatchHistoryItemDto match, string teamName) =>
        NormalizeText(match.AwayTeam) == NormalizeText(teamName);

    private static double GetTeamMetric(MatchHistoryItemDto match, string teamName, TeamMetric metric)
    {
        if (IsTeamHomeInMatch(match, teamName))
        {
            return metric switch
            {
                TeamMetric.Corners => match.HomeCorners,
                TeamMetric.Shots => match.HomeShots,
                TeamMetric.ShotsOnGoal => match.HomeShotsOnGoal,
                TeamMetric.Possession => match.HomePossession,
                _ => 0
            };
        }

        if (IsTeamAwayInMatch(match, teamName))
        {
            return metric switch
            {
                TeamMetric.Corners => match.AwayCorners,
                TeamMetric.Shots => match.AwayShots,
                TeamMetric.ShotsOnGoal => match.AwayShotsOnGoal,
                TeamMetric.Possession => match.AwayPossession,
                _ => 0
            };
        }

        return metric switch
        {
            TeamMetric.Corners => match.HomeCorners,
            TeamMetric.Shots => match.HomeShots,
            TeamMetric.ShotsOnGoal => match.HomeShotsOnGoal,
            TeamMetric.Possession => match.HomePossession,
            _ => 0
        };
    }

    private static double GetOpponentMetric(MatchHistoryItemDto match, string teamName, TeamMetric metric)
    {
        if (IsTeamHomeInMatch(match, teamName))
        {
            return metric switch
            {
                TeamMetric.Corners => match.AwayCorners,
                TeamMetric.Shots => match.AwayShots,
                TeamMetric.ShotsOnGoal => match.AwayShotsOnGoal,
                TeamMetric.Possession => match.AwayPossession,
                _ => 0
            };
        }

        if (IsTeamAwayInMatch(match, teamName))
        {
            return metric switch
            {
                TeamMetric.Corners => match.HomeCorners,
                TeamMetric.Shots => match.HomeShots,
                TeamMetric.ShotsOnGoal => match.HomeShotsOnGoal,
                TeamMetric.Possession => match.HomePossession,
                _ => 0
            };
        }

        return metric switch
        {
            TeamMetric.Corners => match.AwayCorners,
            TeamMetric.Shots => match.AwayShots,
            TeamMetric.ShotsOnGoal => match.AwayShotsOnGoal,
            TeamMetric.Possession => match.AwayPossession,
            _ => 0
        };
    }

    private static double GetTeamGoals(MatchHistoryItemDto match, string teamName)
    {
        if (IsTeamHomeInMatch(match, teamName))
        {
            return match.HomeGoals;
        }

        if (IsTeamAwayInMatch(match, teamName))
        {
            return match.AwayGoals;
        }

        return match.HomeGoals;
    }

    private static double GetOpponentGoals(MatchHistoryItemDto match, string teamName)
    {
        if (IsTeamHomeInMatch(match, teamName))
        {
            return match.AwayGoals;
        }

        if (IsTeamAwayInMatch(match, teamName))
        {
            return match.HomeGoals;
        }

        return match.AwayGoals;
    }

    private static (string? formation, DateOnly matchDate, string opponent)? FindLatestFormation(
        IReadOnlyList<MatchHistoryItemDto> matches,
        string teamName)
    {
        var normalizedTeam = NormalizeText(teamName);
        foreach (var match in matches
                     .OrderByDescending(match => match.MatchDate)
                     .ThenByDescending(match => match.Id))
        {
            if (NormalizeText(match.HomeTeam) == normalizedTeam && IsKnownFormation(match.HomeFormation))
            {
                return (match.HomeFormation, match.MatchDate, match.AwayTeam);
            }

            if (NormalizeText(match.AwayTeam) == normalizedTeam && IsKnownFormation(match.AwayFormation))
            {
                return (match.AwayFormation, match.MatchDate, match.HomeTeam);
            }
        }

        return null;
    }

    private static int GetBig3Flag(IReadOnlyList<TeamBi3InfoDto> teams, string teamName)
    {
        return teams.Any(team => NormalizeText(team.Team) == NormalizeText(teamName) && team.IsBig3) ? 1 : 0;
    }

    private static string DetermineSeason(UpcomingOddsRecord odds)
    {
        var matchDate = odds.MatchDate;
        if (IsLikelyInternationalCompetition(odds.EffectiveLeague))
        {
            return matchDate.Year.ToString(CultureInfo.InvariantCulture);
        }

        return matchDate.Month >= 7
            ? $"{matchDate.Year}-{matchDate.Year + 1}"
            : $"{matchDate.Year - 1}-{matchDate.Year}";
    }

    private static bool IsLikelyInternationalCompetition(string league)
    {
        var value = NormalizeText(league);
        return value.Contains("world cup", StringComparison.Ordinal)
            || value.Contains("copa del mundo", StringComparison.Ordinal)
            || value.Contains("friendly", StringComparison.Ordinal)
            || value.Contains("amistoso", StringComparison.Ordinal)
            || value.Contains("qualifying", StringComparison.Ordinal)
            || value.Contains("eliminatorias", StringComparison.Ordinal)
            || value.Contains("gold cup", StringComparison.Ordinal)
            || value.Contains("africa cup", StringComparison.Ordinal)
            || value.Contains("nations league", StringComparison.Ordinal)
            || value.Contains("european championship", StringComparison.Ordinal)
            || value.Contains("intercontinental", StringComparison.Ordinal);
    }

    private static bool IsLikelyKnockoutCompetition(string league)
    {
        var value = NormalizeText(league);
        return value.Contains("cup", StringComparison.Ordinal)
            || value.Contains("copa", StringComparison.Ordinal)
            || value.Contains("supercup", StringComparison.Ordinal)
            || value.Contains("super cup", StringComparison.Ordinal)
            || value.Contains("pokal", StringComparison.Ordinal)
            || value.Contains("beker", StringComparison.Ordinal)
            || value.Contains("champions league", StringComparison.Ordinal)
            || value.Contains("europa league", StringComparison.Ordinal)
            || value.Contains("conference league", StringComparison.Ordinal)
            || value.Contains("libertadores", StringComparison.Ordinal)
            || value.Contains("sudamericana", StringComparison.Ordinal)
            || value.Contains("leagues cup", StringComparison.Ordinal)
            || value.Contains("world cup", StringComparison.Ordinal)
            || value.Contains("copa del mundo", StringComparison.Ordinal)
            || value.Contains("community shield", StringComparison.Ordinal)
            || value.Contains("campeon de campeones", StringComparison.Ordinal);
    }

    private static bool IsKnownFormation(string? value)
    {
        var normalized = NormalizeText(value);
        return !string.IsNullOrWhiteSpace(normalized)
            && normalized != "unknown"
            && normalized != "null";
    }

    private static string NormalizeText(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static double Average(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0 : Round(array.Sum() / array.Length);
    }

    private static double StandardDeviation(IEnumerable<double> values)
    {
        var numbers = values.Where(value => double.IsFinite(value)).ToArray();
        if (numbers.Length == 0)
        {
            return 0;
        }

        var mean = numbers.Average();
        var variance = numbers.Sum(value => Math.Pow(value - mean, 2)) / numbers.Length;
        return Round(Math.Sqrt(variance));
    }

    private static double Range(IEnumerable<double> values)
    {
        var numbers = values.Where(value => double.IsFinite(value)).ToArray();
        return numbers.Length == 0 ? 0 : Round(numbers.Max() - numbers.Min());
    }

    private static double Round(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double ToDouble(object? value) =>
        value switch
        {
            null => 0,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
        };

    private enum TeamMetric
    {
        Corners,
        Shots,
        ShotsOnGoal,
        Possession
    }
}
