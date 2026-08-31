namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface IRiskAdjustedStakeService
{
    RiskAdjustedStakeResult Recommend(
        decimal originalStake,
        RobustnessComponents components,
        RiskAdjustedStakeOptions options);
}

public sealed class RiskAdjustedStakeService : IRiskAdjustedStakeService
{
    public RiskAdjustedStakeResult Recommend(
        decimal originalStake,
        RobustnessComponents components,
        RiskAdjustedStakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(options);
        Validate(originalStake, options);

        var effective = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(RobustnessComponents.RobustEdgeScore)] = ClampOrZero(components.RobustEdgeScore),
            [nameof(RobustnessComponents.RobustExpectedValueScore)] = ClampOrZero(components.RobustExpectedValueScore),
            [nameof(RobustnessComponents.PositiveEvStability)] = ClampOrZero(components.PositiveEvStability),
            [nameof(RobustnessComponents.CalibrationReliability)] = ClampOrZero(components.CalibrationReliability),
            [nameof(RobustnessComponents.ScenarioStability)] = ClampOrZero(components.ScenarioStability),
            [nameof(RobustnessComponents.ConsensusQuality)] = ClampOrZero(components.ConsensusQuality),
            [nameof(RobustnessComponents.Coherence)] = ClampOrZero(components.Coherence),
            [nameof(RobustnessComponents.DataQuality)] = ClampOrZero(components.DataQuality),
            [nameof(RobustnessComponents.OddsReliability)] = ClampOrZero(components.OddsReliability)
        };
        var recognizedWeights = options.ComponentWeights
            .Where(item => effective.ContainsKey(item.Key) && item.Value > 0m)
            .ToArray();
        if (recognizedWeights.Length == 0)
        {
            throw new ArgumentException("At least one positive, recognized component weight is required.", nameof(options));
        }

        var weightSum = recognizedWeights.Sum(item => item.Value);
        var score = recognizedWeights.Sum(item => item.Value * effective[item.Key]) / weightSum;
        var multiplier = score >= options.HighRobustnessThreshold
            ? options.HighMultiplier
            : score >= options.MediumRobustnessThreshold
                ? options.MediumMultiplier
                : score >= options.MinimumRobustnessThreshold
                    ? options.MinimumMultiplier
                    : 0m;

        // Version 1 is deliberately monotonic: AllowIncrease is retained as configuration data,
        // but an increase is never executed by this implementation.
        multiplier = Math.Clamp(multiplier, 0m, 1m);
        var recommended = Math.Min(originalStake, Math.Min(options.MaximumStake, originalStake * multiplier));
        return new RiskAdjustedStakeResult(
            score,
            originalStake,
            recommended,
            originalStake > 0m ? recommended / originalStake : 0m,
            effective);
    }

    private static decimal ClampOrZero(decimal? value) => Math.Clamp(value ?? 0m, 0m, 1m);

    private static void Validate(decimal originalStake, RiskAdjustedStakeOptions options)
    {
        if (originalStake < 0m
            || options.MaximumStake < 0m
            || options.MinimumRobustnessThreshold < 0m
            || options.MinimumRobustnessThreshold > options.MediumRobustnessThreshold
            || options.MediumRobustnessThreshold > options.HighRobustnessThreshold
            || options.HighRobustnessThreshold > 1m
            || options.HighMultiplier < 0m
            || options.MediumMultiplier < 0m
            || options.MinimumMultiplier < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid risk-adjusted stake options.");
        }
    }
}
