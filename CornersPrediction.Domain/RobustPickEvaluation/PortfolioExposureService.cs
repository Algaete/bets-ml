namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IPortfolioExposureService
{
    IReadOnlyList<PortfolioAllocation> Allocate(
        IReadOnlyCollection<PortfolioPick> candidates,
        IReadOnlyCollection<PortfolioPick> existingPositions,
        PortfolioExposureOptions options);
}

public sealed class PortfolioExposureService : IPortfolioExposureService
{
    public IReadOnlyList<PortfolioAllocation> Allocate(
        IReadOnlyCollection<PortfolioPick> candidates,
        IReadOnlyCollection<PortfolioPick> existingPositions,
        PortfolioExposureOptions options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existingPositions);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        var accepted = existingPositions
            .Where(item => item.RequestedStake > 0m)
            .Select(item => (Pick: item, Stake: item.RequestedStake))
            .ToList();
        var allocations = new List<PortfolioAllocation>(candidates.Count);
        foreach (var candidate in candidates
                     .OrderByDescending(item => item.RobustnessScore)
                     .ThenBy(item => item.PickKey, StringComparer.Ordinal))
        {
            if (candidate.RequestedStake < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(candidates), "Requested stake cannot be negative.");
            }

            var reasons = new HashSet<RobustReasonCode>();
            var remaining = candidate.RequestedStake;
            Limit(ref remaining, options.MaximumStakePerFixture
                - Sum(accepted, item => item.FixtureId == candidate.FixtureId), reasons, false);
            var homeTeamCapacity = options.MaximumStakePerTeam
                - Sum(accepted, item => HasTeam(item, candidate.HomeTeamKey));
            var awayTeamCapacity = options.MaximumStakePerTeam
                - Sum(accepted, item => HasTeam(item, candidate.AwayTeamKey));
            Limit(ref remaining, Math.Min(homeTeamCapacity, awayTeamCapacity), reasons, false);
            Limit(ref remaining, options.MaximumStakePerLeague
                - Sum(accepted, item => Same(item.LeagueKey, candidate.LeagueKey)), reasons, false);
            Limit(ref remaining, options.MaximumStakePerMarketFamily
                - Sum(accepted, item => item.MarketFamily == candidate.MarketFamily), reasons, false);
            Limit(ref remaining, options.MaximumStakePerBot
                - Sum(accepted, item => Same(item.BotKey, candidate.BotKey)), reasons, false);
            Limit(ref remaining, options.MaximumStakePerDay
                - Sum(accepted, item => item.Day == candidate.Day), reasons, false);
            Limit(ref remaining, options.MaximumStakePerCorrelationCluster
                - Sum(accepted, item => Same(item.CorrelationCluster, candidate.CorrelationCluster)), reasons, true);

            var relatedCount = accepted.Count(item => item.Pick.FixtureId == candidate.FixtureId);
            if (relatedCount >= options.MaximumRelatedPicksPerFixture)
            {
                remaining = 0m;
                reasons.Add(RobustReasonCode.CorrelatedExposureLimitExceeded);
            }

            remaining = Math.Clamp(remaining, 0m, candidate.RequestedStake);
            if (remaining > 0m)
            {
                accepted.Add((candidate, remaining));
            }
            allocations.Add(new PortfolioAllocation(
                candidate,
                remaining,
                remaining <= 0m,
                reasons.OrderBy(reason => reason).ToArray()));
        }

        return allocations;
    }

    private static decimal Sum(
        IEnumerable<(PortfolioPick Pick, decimal Stake)> accepted,
        Func<PortfolioPick, bool> predicate) => accepted
        .Where(item => predicate(item.Pick))
        .Sum(item => item.Stake);

    private static bool HasTeam(PortfolioPick pick, string teamKey) =>
        Same(pick.HomeTeamKey, teamKey) || Same(pick.AwayTeamKey, teamKey);

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void Limit(
        ref decimal remaining,
        decimal capacity,
        ISet<RobustReasonCode> reasons,
        bool correlated)
    {
        if (capacity >= remaining)
        {
            return;
        }

        remaining = Math.Max(0m, capacity);
        reasons.Add(correlated
            ? RobustReasonCode.CorrelatedExposureLimitExceeded
            : RobustReasonCode.ExposureLimitExceeded);
    }

    private static void Validate(PortfolioExposureOptions options)
    {
        if (options.MaximumStakePerFixture < 0m
            || options.MaximumStakePerTeam < 0m
            || options.MaximumStakePerLeague < 0m
            || options.MaximumStakePerMarketFamily < 0m
            || options.MaximumStakePerBot < 0m
            || options.MaximumStakePerDay < 0m
            || options.MaximumStakePerCorrelationCluster < 0m
            || options.MaximumRelatedPicksPerFixture < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Exposure limits cannot be negative.");
        }
    }
}
