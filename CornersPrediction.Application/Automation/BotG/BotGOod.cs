using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public sealed class BotGRobustOodService : IBotGOodService
{
    private const double MadConsistencyScale = 1.4826d;

    public BotGOodResult Evaluate(BotGOodInput input, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration).OutOfDistribution;
        var eligible = input.ReferenceFeatures
            .Where(reference => reference.SampleSize >= config.MinimumReferenceSampleSize)
            .Where(IsFiniteReference)
            .GroupBy(reference => reference.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(reference => reference.SampleSize).First())
            .ToArray();
        if (eligible.Length == 0)
            return Unavailable(config.Version, "No OOD reference feature has enough historical evidence.");

        var missing = eligible
            .Where(reference => !input.NumericFeatures.TryGetValue(reference.Name, out var value) || !double.IsFinite(value))
            .Select(reference => reference.Name)
            .ToArray();
        if (missing.Length > 0)
            return Unavailable(config.Version, $"OOD features are missing or invalid: {string.Join(", ", missing)}.");

        var zScores = new Dictionary<string, double>(StringComparer.Ordinal);
        var outliers = new List<string>();
        var maximumSeverity = 0d;
        foreach (var reference in eligible)
        {
            var value = input.NumericFeatures[reference.Name];
            var percentileScale = Math.Max(1e-9d, (reference.Percentile99 - reference.Percentile01) / 4.652d);
            var robustScale = reference.MedianAbsoluteDeviation > 1e-9d
                ? MadConsistencyScale * reference.MedianAbsoluteDeviation
                : percentileScale;
            var robustZ = Math.Abs(value - reference.Median) / robustScale;
            zScores[reference.Name] = robustZ;
            var outsideReferenceEnvelope = value < reference.Percentile01 || value > reference.Percentile99;
            if (robustZ >= config.RobustZScoreThreshold || outsideReferenceEnvelope)
                outliers.Add(reference.Name);
            var zSeverity = NormalizeSeverity(robustZ, config.RobustZScoreThreshold, config.SevereRobustZScore);
            var envelopeSeverity = outsideReferenceEnvelope
                ? Math.Max(0.25d, zSeverity)
                : 0d;
            maximumSeverity = Math.Max(maximumSeverity, Math.Max(zSeverity, envelopeSeverity));
        }

        return new BotGOodResult(
            true,
            Math.Clamp(maximumSeverity, 0d, 1d),
            zScores,
            outliers.Order(StringComparer.Ordinal).ToArray(),
            config.Version);
    }

    private static bool IsFiniteReference(BotGOodFeatureReference reference) =>
        !string.IsNullOrWhiteSpace(reference.Name)
        && double.IsFinite(reference.Median)
        && double.IsFinite(reference.MedianAbsoluteDeviation)
        && reference.MedianAbsoluteDeviation >= 0d
        && double.IsFinite(reference.Percentile01)
        && double.IsFinite(reference.Percentile99)
        && reference.Percentile99 >= reference.Percentile01;

    private static double NormalizeSeverity(double z, double threshold, double severe) =>
        z <= threshold ? 0d : Math.Clamp((z - threshold) / Math.Max(1e-9d, severe - threshold), 0d, 1d);

    private static BotGOodResult Unavailable(string version, string reason) =>
        new(false, 1d, new Dictionary<string, double>(), [], version, reason);
}
