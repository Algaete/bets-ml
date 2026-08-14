using CornersPrediction.Application.MatchHistory;
using CornersPrediction.Application.Teams;

namespace CornersPredictionApi.NewGenerationMl;

public sealed class NewGenerationFeatureBuilder
{
    private readonly IGetPredictionContextUseCase _predictionContext;
    private readonly IGetTeamBi3InfoUseCase _teamInfo;

    public NewGenerationFeatureBuilder(
        IGetPredictionContextUseCase predictionContext,
        IGetTeamBi3InfoUseCase teamInfo)
    {
        _predictionContext = predictionContext;
        _teamInfo = teamInfo;
    }

    public async Task<NewGenerationFeatureBuildResult> BuildAsync(
        NewGenerationPredictionRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var contextTask = _predictionContext.GetAsync(
            request.HomeTeam.Trim(),
            request.AwayTeam.Trim(),
            string.IsNullOrWhiteSpace(request.League) ? null : request.League.Trim(),
            "M",
            null,
            request.MatchDate,
            cancellationToken);
        var teamInfoTask = _teamInfo.GetAsync(request.League.Trim(), "M", cancellationToken);
        await Task.WhenAll(contextTask, teamInfoTask);
        var context = await contextTask;
        var teams = await teamInfoTask;

        var homeGeneral = Before(context.HomeGeneralMatches, request.MatchDate);
        var homeVenue = Before(context.HomeAsHomeMatches, request.MatchDate);
        var awayGeneral = Before(context.AwayGeneralMatches, request.MatchDate);
        var awayVenue = Before(context.AwayAsAwayMatches, request.MatchDate);
        var missingHistory = new List<string>();
        if (homeGeneral.Count < 5)
        {
            missingHistory.Add($"{request.HomeTeam}: only {homeGeneral.Count} prior general matches; at least 5 are required");
        }
        if (awayGeneral.Count < 5)
        {
            missingHistory.Add($"{request.AwayTeam}: only {awayGeneral.Count} prior general matches; at least 5 are required");
        }
        if (missingHistory.Count > 0)
        {
            throw new ArgumentException("Pre-match history is insufficient. " + string.Join("; ", missingHistory));
        }

        var homeFormation = KnownFormation(request.HomeFormation)
            ? request.HomeFormation!.Trim()
            : LatestFormation(homeGeneral, request.HomeTeam);
        var awayFormation = KnownFormation(request.AwayFormation)
            ? request.AwayFormation!.Trim()
            : LatestFormation(awayGeneral, request.AwayTeam);
        var homeStyle = FormationStyle(homeFormation);
        var awayStyle = FormationStyle(awayFormation);
        var features = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["IsKnockout"] = request.IsKnockout ? 1 : 0,
            ["HomeHistoricBig3"] = Big3(teams, request.HomeTeam),
            ["AwayHistoricBig3"] = Big3(teams, request.AwayTeam),
            ["League"] = request.League.Trim(),
            ["HomeTeam"] = request.HomeTeam.Trim(),
            ["AwayTeam"] = request.AwayTeam.Trim(),
            ["HomeFormationStyle"] = homeStyle,
            ["AwayFormationStyle"] = awayStyle
        };
        AddGeneral(features, "Home", homeGeneral, request.HomeTeam, request.MatchDate);
        AddGeneral(features, "Away", awayGeneral, request.AwayTeam, request.MatchDate);
        AddVenue(features, "Home", "Home", homeVenue, request.HomeTeam);
        AddVenue(features, "Away", "Away", awayVenue, request.AwayTeam);

        var warnings = new List<string>();
        if (homeGeneral.Count < 10 || awayGeneral.Count < 10)
        {
            warnings.Add("One or both teams have fewer than 10 prior matches; the trained pipeline will impute numeric nulls where needed.");
        }
        if (homeStyle == "unknown" || awayStyle == "unknown")
        {
            warnings.Add("At least one formation style is unknown; the categorical pipeline will use its trained unknown category handling.");
        }
        if (homeVenue.Count == 0 || awayVenue.Count == 0)
        {
            warnings.Add("Venue-specific history is unavailable for at least one team.");
        }

        return new NewGenerationFeatureBuildResult(
            features,
            warnings,
            new NewGenerationMatchSummary(
                request.League.Trim(),
                string.IsNullOrWhiteSpace(request.Season) ? null : request.Season.Trim(),
                request.MatchDate,
                request.HomeTeam.Trim(),
                request.AwayTeam.Trim(),
                homeStyle,
                awayStyle));
    }

    private static void Validate(NewGenerationPredictionRequest request)
    {
        if (request.AdditionalFields is { Count: > 0 })
        {
            var targetFields = request.AdditionalFields.Keys
                .Where(name => name.StartsWith("Target", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (targetFields.Length > 0)
            {
                throw new ArgumentException(
                    "Target fields are forbidden in Models 2026 requests: " +
                    string.Join(", ", targetFields));
            }
            throw new ArgumentException(
                "Unexpected Models 2026 request fields: " +
                string.Join(", ", request.AdditionalFields.Keys));
        }
        if (string.IsNullOrWhiteSpace(request.League) ||
            string.IsNullOrWhiteSpace(request.HomeTeam) ||
            string.IsNullOrWhiteSpace(request.AwayTeam))
        {
            throw new ArgumentException("League, HomeTeam and AwayTeam are required.");
        }
        if (request.HomeTeam.Trim().Equals(request.AwayTeam.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("HomeTeam and AwayTeam must be different.");
        }
        if (request.MatchDate == default)
        {
            throw new ArgumentException("MatchDate is required.");
        }
    }

    private static IReadOnlyList<MatchHistoryItemDto> Before(
        IReadOnlyList<MatchHistoryItemDto> matches,
        DateOnly matchDate) => matches
            .Where(match => match.MatchDate < matchDate)
            .GroupBy(match => match.Id)
            .Select(group => group.First())
            .OrderByDescending(match => match.MatchDate)
            .ThenByDescending(match => match.Id)
            .Take(10)
            .ToArray();

    private static void AddGeneral(
        IDictionary<string, object?> values,
        string prefix,
        IReadOnlyList<MatchHistoryItemDto> matches,
        string team,
        DateOnly matchDate)
    {
        var last5 = matches.Take(5).ToArray();
        var last10 = matches.Take(10).ToArray();
        var corners5 = Metric(last5, team, MetricKind.Corners, forTeam: true);
        var corners10 = Metric(last10, team, MetricKind.Corners, forTeam: true);
        var shots5 = Metric(last5, team, MetricKind.Shots, forTeam: true);
        var shots10 = Metric(last10, team, MetricKind.Shots, forTeam: true);
        var sog5 = Metric(last5, team, MetricKind.ShotsOnGoal, forTeam: true);
        var sog10 = Metric(last10, team, MetricKind.ShotsOnGoal, forTeam: true);
        var goals5 = Metric(last5, team, MetricKind.Goals, forTeam: true);
        var goals10 = Metric(last10, team, MetricKind.Goals, forTeam: true);

        values[$"{prefix}AvgCornersFor5"] = Average(corners5);
        values[$"{prefix}AvgCornersFor10"] = Average(corners10);
        values[$"{prefix}MedianCornersFor10"] = Median(corners10);
        values[$"{prefix}AvgCornersAgainst10"] = Average(Metric(last10, team, MetricKind.Corners, forTeam: false));
        values[$"{prefix}StdDevCornersFor10"] = StandardDeviation(corners10);
        values[$"{prefix}AvgShotsFor5"] = Average(shots5);
        values[$"{prefix}AvgShotsFor10"] = Average(shots10);
        values[$"{prefix}MedianShotsFor10"] = Median(shots10);
        values[$"{prefix}AvgShotsAgainst10"] = Average(Metric(last10, team, MetricKind.Shots, forTeam: false));
        values[$"{prefix}AvgShotsOnGoalFor5"] = Average(sog5);
        values[$"{prefix}AvgShotsOnGoalFor10"] = Average(sog10);
        values[$"{prefix}MedianShotsOnGoalFor10"] = Median(sog10);
        values[$"{prefix}AvgShotsOnGoalAgainst10"] = Average(Metric(last10, team, MetricKind.ShotsOnGoal, forTeam: false));
        values[$"{prefix}ShotAccuracy10"] = Ratio(sog10.Sum(), shots10.Sum());
        values[$"{prefix}AvgPossession10"] = Average(Metric(last10, team, MetricKind.Possession, forTeam: true));
        values[$"{prefix}AvgGoalsFor5"] = Average(goals5);
        values[$"{prefix}AvgGoalsFor10"] = Average(goals10);
        values[$"{prefix}MedianGoalsFor10"] = Median(goals10);
        values[$"{prefix}AvgGoalsAgainst10"] = Average(Metric(last10, team, MetricKind.Goals, forTeam: false));
        values[$"{prefix}PointsPerMatch5"] = Average(last5.Select(match => Points(match, team)).ToArray());
        values[$"{prefix}PointsPerMatch10"] = Average(last10.Select(match => Points(match, team)).ToArray());
        values[$"{prefix}DaysRest"] = last10.Length == 0
            ? null
            : matchDate.DayNumber - last10.Max(match => match.MatchDate).DayNumber;
    }

    private static void AddVenue(
        IDictionary<string, object?> values,
        string prefix,
        string venue,
        IReadOnlyList<MatchHistoryItemDto> matches,
        string team)
    {
        var last5 = matches.Take(5).ToArray();
        values[$"{prefix}Avg{venue}CornersFor5"] = Average(Metric(last5, team, MetricKind.Corners, true));
        values[$"{prefix}Avg{venue}CornersAgainst5"] = Average(Metric(last5, team, MetricKind.Corners, false));
        values[$"{prefix}Avg{venue}GoalsFor5"] = Average(Metric(last5, team, MetricKind.Goals, true));
        values[$"{prefix}Avg{venue}GoalsAgainst5"] = Average(Metric(last5, team, MetricKind.Goals, false));
    }

    private static double[] Metric(
        IEnumerable<MatchHistoryItemDto> matches,
        string team,
        MetricKind kind,
        bool forTeam) => matches.Select(match => SideValue(match, team, kind, forTeam)).ToArray();

    private static double SideValue(MatchHistoryItemDto match, string team, MetricKind kind, bool forTeam)
    {
        var isHome = SameTeam(match.HomeTeam, team);
        var useHome = forTeam ? isHome : !isHome;
        return (kind, useHome) switch
        {
            (MetricKind.Corners, true) => match.HomeCorners,
            (MetricKind.Corners, false) => match.AwayCorners,
            (MetricKind.Shots, true) => match.HomeShots,
            (MetricKind.Shots, false) => match.AwayShots,
            (MetricKind.ShotsOnGoal, true) => match.HomeShotsOnGoal,
            (MetricKind.ShotsOnGoal, false) => match.AwayShotsOnGoal,
            (MetricKind.Possession, true) => match.HomePossession,
            (MetricKind.Possession, false) => match.AwayPossession,
            (MetricKind.Goals, true) => match.HomeGoals,
            (MetricKind.Goals, false) => match.AwayGoals,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static double Points(MatchHistoryItemDto match, string team)
    {
        var goalsFor = SideValue(match, team, MetricKind.Goals, true);
        var goalsAgainst = SideValue(match, team, MetricKind.Goals, false);
        return goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0;
    }

    private static double? Average(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : values.Average();

    private static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }

    private static double? StandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2)
        {
            return null;
        }
        var mean = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1));
    }

    private static double? Ratio(double numerator, double denominator) => denominator <= 0 ? null : numerator / denominator;

    private static int Big3(IEnumerable<TeamBi3InfoDto> teams, string team) =>
        teams.Any(item => item.IsBig3 && SameTeam(item.Team, team)) ? 1 : 0;

    private static string LatestFormation(IEnumerable<MatchHistoryItemDto> matches, string team)
    {
        foreach (var match in matches)
        {
            var formation = SameTeam(match.HomeTeam, team) ? match.HomeFormation : match.AwayFormation;
            if (KnownFormation(formation))
            {
                return formation!.Trim();
            }
        }
        return "unknown";
    }

    private static string FormationStyle(string? formation)
    {
        if (!KnownFormation(formation))
        {
            return "unknown";
        }
        var digit = formation!.FirstOrDefault(char.IsDigit);
        return digit switch
        {
            '5' or '6' => "defensive",
            '1' or '2' or '3' => "aggressive",
            _ => "normal"
        };
    }

    private static bool KnownFormation(string? formation) =>
        !string.IsNullOrWhiteSpace(formation) &&
        !formation.Trim().Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
        !formation.Trim().Equals("null", StringComparison.OrdinalIgnoreCase);

    private static bool SameTeam(string left, string right) =>
        TeamNameMatcher.AreEquivalent(left, right);

    private enum MetricKind
    {
        Corners,
        Shots,
        ShotsOnGoal,
        Possession,
        Goals
    }
}

public sealed record NewGenerationFeatureBuildResult(
    IReadOnlyDictionary<string, object?> Features,
    IReadOnlyList<string> Warnings,
    NewGenerationMatchSummary Match);
