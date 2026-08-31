namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IRobustValueEvaluationService
{
    RobustValueEvaluationResult Evaluate(
        decimal pointModelProbability,
        decimal conservativeMarketProbability,
        decimal decimalOdds,
        IReadOnlyCollection<RobustScenarioValue> scenarios);
}

public sealed class RobustValueEvaluationService : IRobustValueEvaluationService
{
    public RobustValueEvaluationResult Evaluate(
        decimal pointModelProbability,
        decimal conservativeMarketProbability,
        decimal decimalOdds,
        IReadOnlyCollection<RobustScenarioValue> scenarios)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (pointModelProbability is < 0m or > 1m
            || conservativeMarketProbability is < 0m or > 1m
            || decimalOdds <= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(pointModelProbability), "Invalid probability or odds.");
        }

        var pointEdge = pointModelProbability - conservativeMarketProbability;
        var pointEv = pointModelProbability * (decimalOdds - 1m) - (1m - pointModelProbability);
        var valid = scenarios
            .Where(scenario => scenario.IsUsable
                && scenario.ProbabilityWeight > 0m
                && scenario.ModelFairProbability is >= 0m and <= 1m
                && scenario.EvidenceStatus is not EvidenceStatus.InsufficientEvidence
                    and not EvidenceStatus.SourceUnavailable
                    and not EvidenceStatus.SnapshotExpired)
            .ToArray();
        if (valid.Length == 0)
        {
            return new RobustValueEvaluationResult(
                pointEdge,
                pointEv,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                0m,
                0m,
                0,
                [RobustReasonCode.EvidenceInsufficient]);
        }

        var weightSum = valid.Sum(item => item.ProbabilityWeight);
        var probabilityP10 = WeightedQuantile(
            valid.Select(item => (item.ModelFairProbability, item.ProbabilityWeight)),
            0.10m);
        var edgeP10 = WeightedQuantile(
            valid.Select(item => (item.ModelFairProbability - conservativeMarketProbability, item.ProbabilityWeight)),
            0.10m);
        var edgeP50 = WeightedQuantile(
            valid.Select(item => (item.ModelFairProbability - conservativeMarketProbability, item.ProbabilityWeight)),
            0.50m);
        var edgeP90 = WeightedQuantile(
            valid.Select(item => (item.ModelFairProbability - conservativeMarketProbability, item.ProbabilityWeight)),
            0.90m);
        var evP10 = WeightedQuantile(
            valid.Select(item => (item.ExpectedValue, item.ProbabilityWeight)),
            0.10m);
        var evP50 = WeightedQuantile(
            valid.Select(item => (item.ExpectedValue, item.ProbabilityWeight)),
            0.50m);
        var evP90 = WeightedQuantile(
            valid.Select(item => (item.ExpectedValue, item.ProbabilityWeight)),
            0.90m);
        var positiveStability = valid
            .Where(item => item.ExpectedValue > 0m)
            .Sum(item => item.ProbabilityWeight) / weightSum;
        var sideStability = valid
            .Where(item => item.RetainsOriginalSide)
            .Sum(item => item.ProbabilityWeight) / weightSum;

        return new RobustValueEvaluationResult(
            pointEdge,
            pointEv,
            probabilityP10,
            probabilityP10 - conservativeMarketProbability,
            evP10,
            evP10,
            evP50,
            evP90,
            edgeP10,
            edgeP50,
            edgeP90,
            positiveStability,
            sideStability,
            valid.Length,
            []);
    }

    private static decimal WeightedQuantile(
        IEnumerable<(decimal Value, decimal Weight)> values,
        decimal probability)
    {
        var ordered = values.OrderBy(item => item.Value).ToArray();
        var target = ordered.Sum(item => item.Weight) * probability;
        var cumulative = 0m;
        foreach (var item in ordered)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
            {
                return item.Value;
            }
        }
        return ordered[^1].Value;
    }
}
