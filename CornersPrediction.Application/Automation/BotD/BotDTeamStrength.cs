using CornersPrediction.Application.Teams;

namespace CornersPrediction.Application.Automation.BotD;

public sealed record BotDTeamResultObservation(
    int MatchId,
    DateTime MatchDateUtc,
    string HomeTeam,
    string AwayTeam,
    int HomeGoals,
    int AwayGoals);

public sealed record BotDTeamStrengthConfiguration
{
    public bool Enabled { get; init; }
    public string Version { get; init; } = "bot-d-team-strength-1.0.0";
    public double ResultDecayFactor { get; init; } = 0.90d;
    public double EloKFactor { get; init; } = 24d;
    public double HomeAdvantageElo { get; init; } = 50d;
    public double EloWeight { get; init; } = 0.50d;
    public double DirectMatchWeight { get; init; } = 0.20d;
    public double CommonOpponentWeight { get; init; } = 0.30d;
    public int MinimumMatchesPerTeam { get; init; } = 4;
    public int MinimumCommonOpponents { get; init; } = 1;
    public double MinimumConfidenceScore { get; init; } = 0.45d;
    public double MaximumProbabilityAdjustment { get; init; } = 0.08d;
    public double ContextExpectedValueSigmaWeight { get; init; } = 0.35d;
    public double HomeTeamMarketWeight { get; init; } = 1d;
    public double AwayTeamMarketWeight { get; init; } = 0.80d;
    public double TotalMarketWeight { get; init; } = 0.15d;
}

public sealed record BotDTeamStrengthResult(
    bool IsAvailable,
    int InputMatches,
    int AcceptedMatches,
    int HomeTeamMatches,
    int AwayTeamMatches,
    int DirectMatches,
    int CommonOpponents,
    double HomeElo,
    double AwayElo,
    double EloGap,
    double EloSignal,
    double DirectMatchSignal,
    double CommonOpponentSignal,
    double RawStrengthGap,
    double ConfidenceScore,
    double AdjustedStrengthGap,
    IReadOnlyList<string> RiskFlags)
{
    public static BotDTeamStrengthResult Disabled(int inputMatches) =>
        new(false, inputMatches, 0, 0, 0, 0, 0, 1500d, 1500d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, []);
}

public static class BotDTeamStrengthCalculator
{
    public static BotDTeamStrengthResult Calculate(
        string homeTeam,
        string awayTeam,
        DateTime asOfDateUtc,
        IReadOnlyList<BotDTeamResultObservation>? observations,
        BotDTeamStrengthConfiguration configuration)
    {
        Validate(configuration);
        var input = observations ?? [];
        if (!configuration.Enabled)
        {
            return BotDTeamStrengthResult.Disabled(input.Count);
        }

        if (string.IsNullOrWhiteSpace(homeTeam) || string.IsNullOrWhiteSpace(awayTeam)
            || TeamNameMatcher.AreEquivalent(homeTeam, awayTeam))
        {
            return Unavailable(input.Count, "InvalidCandidateTeams");
        }

        var asOfUtc = EnsureUtc(asOfDateUtc);
        var identities = new List<string> { homeTeam.Trim(), awayTeam.Trim() };
        var homeKey = identities[0];
        var awayKey = identities[1];
        var accepted = input
            .Where(match => EnsureUtc(match.MatchDateUtc) < asOfUtc)
            .Where(match => !string.IsNullOrWhiteSpace(match.HomeTeam) && !string.IsNullOrWhiteSpace(match.AwayTeam))
            .Where(match => match.HomeGoals >= 0 && match.AwayGoals >= 0)
            .Select(match => new StrengthMatch(
                match.MatchId,
                EnsureUtc(match.MatchDateUtc),
                ResolveIdentity(match.HomeTeam, identities),
                ResolveIdentity(match.AwayTeam, identities),
                match.HomeGoals,
                match.AwayGoals))
            .Where(match => !match.HomeTeam.Equals(match.AwayTeam, StringComparison.OrdinalIgnoreCase))
            .GroupBy(MatchIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(match => match.MatchDateUtc)
            .ThenBy(match => match.MatchId)
            .ToArray();

        var homeMatches = accepted.Where(match => Contains(match, homeKey)).ToArray();
        var awayMatches = accepted.Where(match => Contains(match, awayKey)).ToArray();
        var risks = new List<string>();
        if (homeMatches.Length < configuration.MinimumMatchesPerTeam
            || awayMatches.Length < configuration.MinimumMatchesPerTeam)
        {
            risks.Add("InsufficientTeamStrengthHistory");
            return new BotDTeamStrengthResult(
                false, input.Count, accepted.Length, homeMatches.Length, awayMatches.Length,
                0, 0, 1500d, 1500d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, risks);
        }

        var ratings = CalculateElo(accepted, configuration);
        var homeElo = ratings.GetValueOrDefault(homeKey, 1500d);
        var awayElo = ratings.GetValueOrDefault(awayKey, 1500d);
        var eloGap = homeElo - awayElo;
        var eloSignal = Math.Tanh(eloGap / 300d);

        var direct = accepted
            .Where(match => Contains(match, homeKey) && Contains(match, awayKey))
            .OrderByDescending(match => match.MatchDateUtc)
            .ToArray();
        var directSignal = WeightedPerformance(direct, homeKey, configuration.ResultDecayFactor);

        var homeByOpponent = GroupByOpponent(homeMatches, homeKey);
        var awayByOpponent = GroupByOpponent(awayMatches, awayKey);
        var commonSignals = new List<double>();
        foreach (var homeOpponent in homeByOpponent)
        {
            if (homeOpponent.Key.Equals(awayKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var awayOpponent = awayByOpponent.FirstOrDefault(pair =>
                pair.Key.Equals(homeOpponent.Key, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(awayOpponent.Key))
            {
                continue;
            }

            var homePerformance = WeightedPerformance(
                homeOpponent.Value.OrderByDescending(match => match.MatchDateUtc),
                homeKey,
                configuration.ResultDecayFactor);
            var awayPerformance = WeightedPerformance(
                awayOpponent.Value.OrderByDescending(match => match.MatchDateUtc),
                awayKey,
                configuration.ResultDecayFactor);
            commonSignals.Add(Math.Clamp((homePerformance - awayPerformance) / 2d, -1d, 1d));
        }

        var commonOpponentSignal = commonSignals.Count == 0 ? 0d : commonSignals.Average();
        if (direct.Length == 0)
        {
            risks.Add("NoDirectMeetings");
        }
        if (commonSignals.Count < configuration.MinimumCommonOpponents)
        {
            risks.Add("InsufficientCommonOpponents");
        }

        var usedWeight = configuration.EloWeight;
        var weightedSignal = configuration.EloWeight * eloSignal;
        if (direct.Length > 0)
        {
            usedWeight += configuration.DirectMatchWeight;
            weightedSignal += configuration.DirectMatchWeight * directSignal;
        }
        if (commonSignals.Count > 0)
        {
            usedWeight += configuration.CommonOpponentWeight;
            weightedSignal += configuration.CommonOpponentWeight * commonOpponentSignal;
        }

        var rawGap = usedWeight <= 0 ? 0d : Math.Clamp(weightedSignal / usedWeight, -1d, 1d);
        var sampleConfidence = Math.Clamp(
            Math.Min(homeMatches.Length, awayMatches.Length) / (double)configuration.MinimumMatchesPerTeam,
            0d,
            1d);
        var linkedEvidence = direct.Length + commonSignals.Count;
        var linkConfidence = 0.65d + 0.35d * Math.Clamp(linkedEvidence / 3d, 0d, 1d);
        var confidence = Math.Clamp(sampleConfidence * linkConfidence, 0d, 1d);
        var adjustedGap = Math.Clamp(rawGap * confidence, -1d, 1d);

        return new BotDTeamStrengthResult(
            true,
            input.Count,
            accepted.Length,
            homeMatches.Length,
            awayMatches.Length,
            direct.Length,
            commonSignals.Count,
            homeElo,
            awayElo,
            eloGap,
            eloSignal,
            directSignal,
            commonOpponentSignal,
            rawGap,
            confidence,
            adjustedGap,
            risks);
    }

    public static BotDTeamStrengthConfiguration Validate(BotDTeamStrengthConfiguration value)
    {
        RequireRange(value.ResultDecayFactor, 0.50d, 0.999d, nameof(value.ResultDecayFactor));
        RequireRange(value.EloKFactor, 1d, 100d, nameof(value.EloKFactor));
        RequireRange(value.HomeAdvantageElo, 0d, 200d, nameof(value.HomeAdvantageElo));
        RequireRange(value.MinimumConfidenceScore, 0d, 1d, nameof(value.MinimumConfidenceScore));
        RequireRange(value.MaximumProbabilityAdjustment, 0d, 0.25d, nameof(value.MaximumProbabilityAdjustment));
        RequireRange(value.ContextExpectedValueSigmaWeight, 0d, 2d, nameof(value.ContextExpectedValueSigmaWeight));
        RequireRange(value.HomeTeamMarketWeight, 0d, 2d, nameof(value.HomeTeamMarketWeight));
        RequireRange(value.AwayTeamMarketWeight, 0d, 2d, nameof(value.AwayTeamMarketWeight));
        RequireRange(value.TotalMarketWeight, 0d, 1d, nameof(value.TotalMarketWeight));
        if (value.MinimumMatchesPerTeam is < 1 or > 100 || value.MinimumCommonOpponents is < 0 or > 20)
        {
            throw new ArgumentException("Bot D team-strength sample thresholds are invalid.");
        }

        var weights = value.EloWeight + value.DirectMatchWeight + value.CommonOpponentWeight;
        if (value.EloWeight < 0 || value.DirectMatchWeight < 0 || value.CommonOpponentWeight < 0
            || Math.Abs(weights - 1d) > 0.0001d)
        {
            throw new ArgumentException("Bot D team-strength weights must be non-negative and add up to 1.0.");
        }

        return value;
    }

    private static Dictionary<string, double> CalculateElo(
        IReadOnlyList<StrengthMatch> matches,
        BotDTeamStrengthConfiguration configuration)
    {
        var ratings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var homeRating = ratings.GetValueOrDefault(match.HomeTeam, 1500d);
            var awayRating = ratings.GetValueOrDefault(match.AwayTeam, 1500d);
            var expectedHome = 1d / (1d + Math.Pow(
                10d,
                (awayRating - (homeRating + configuration.HomeAdvantageElo)) / 400d));
            var actualHome = match.HomeGoals == match.AwayGoals ? 0.5d : match.HomeGoals > match.AwayGoals ? 1d : 0d;
            var marginMultiplier = 1d + 0.25d * Math.Log(1d + Math.Abs(match.HomeGoals - match.AwayGoals));
            var recencyWeight = Math.Pow(configuration.ResultDecayFactor, matches.Count - index - 1);
            var change = configuration.EloKFactor * marginMultiplier * recencyWeight * (actualHome - expectedHome);
            ratings[match.HomeTeam] = homeRating + change;
            ratings[match.AwayTeam] = awayRating - change;
        }
        return ratings;
    }

    private static Dictionary<string, List<StrengthMatch>> GroupByOpponent(
        IEnumerable<StrengthMatch> matches,
        string team) =>
        matches
            .Select(match => new { Match = match, Opponent = Opponent(match, team) })
            .Where(value => value.Opponent is not null)
            .GroupBy(value => value.Opponent!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.Match).ToList(),
                StringComparer.OrdinalIgnoreCase);

    private static double WeightedPerformance(
        IEnumerable<StrengthMatch> matches,
        string team,
        double decayFactor)
    {
        var rows = matches.ToArray();
        if (rows.Length == 0)
        {
            return 0d;
        }

        double total = 0d;
        double weights = 0d;
        for (var index = 0; index < rows.Length; index++)
        {
            var match = rows[index];
            var teamIsHome = match.HomeTeam.Equals(team, StringComparison.OrdinalIgnoreCase);
            var goalsFor = teamIsHome ? match.HomeGoals : match.AwayGoals;
            var goalsAgainst = teamIsHome ? match.AwayGoals : match.HomeGoals;
            var difference = goalsFor - goalsAgainst;
            var outcome = difference == 0 ? 0d : difference > 0 ? 0.75d : -0.75d;
            var performance = Math.Clamp(
                outcome + Math.Sign(difference) * Math.Min(3, Math.Abs(difference)) / 12d,
                -1d,
                1d);
            var weight = Math.Pow(decayFactor, index);
            total += performance * weight;
            weights += weight;
        }
        return weights <= 0 ? 0d : total / weights;
    }

    private static string? Opponent(StrengthMatch match, string team)
    {
        if (match.HomeTeam.Equals(team, StringComparison.OrdinalIgnoreCase)) return match.AwayTeam;
        if (match.AwayTeam.Equals(team, StringComparison.OrdinalIgnoreCase)) return match.HomeTeam;
        return null;
    }

    private static bool Contains(StrengthMatch match, string team) =>
        match.HomeTeam.Equals(team, StringComparison.OrdinalIgnoreCase)
        || match.AwayTeam.Equals(team, StringComparison.OrdinalIgnoreCase);

    private static string ResolveIdentity(string team, ICollection<string> identities)
    {
        var identity = identities.FirstOrDefault(candidate => TeamNameMatcher.AreEquivalent(candidate, team));
        if (identity is not null)
        {
            return identity;
        }

        var value = team.Trim();
        identities.Add(value);
        return value;
    }

    private static string MatchIdentity(StrengthMatch match) => match.MatchId > 0
        ? $"id:{match.MatchId}"
        : $"{match.MatchDateUtc:O}|{match.HomeTeam}|{match.AwayTeam}|{match.HomeGoals}|{match.AwayGoals}";

    private static BotDTeamStrengthResult Unavailable(int inputMatches, string risk) =>
        new(false, inputMatches, 0, 0, 0, 0, 0, 1500d, 1500d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, [risk]);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        }
    }

    private sealed record StrengthMatch(
        int MatchId,
        DateTime MatchDateUtc,
        string HomeTeam,
        string AwayTeam,
        int HomeGoals,
        int AwayGoals);
}
