namespace CornersPrediction.Domain.RobustPickEvaluation;

public interface ICalibrationReliabilityService
{
    CalibrationReliabilityResult Evaluate(
        CalibrationReliabilityInput input,
        CalibrationReliabilityOptions options);
}

public sealed class CalibrationReliabilityService : ICalibrationReliabilityService
{
    public CalibrationReliabilityResult Evaluate(
        CalibrationReliabilityInput input,
        CalibrationReliabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        Validate(input, options);

        var sample = input.EffectiveN.HasValue
            ? Clamp01(input.EffectiveN.Value / options.TargetEffectiveN)
            : 0m;
        var specificity = input.FallbackLevel switch
        {
            CalibrationFallbackLevel.ExactMarket => options.ExactMarketSpecificity,
            CalibrationFallbackLevel.MarketFamily => options.FamilySpecificity,
            CalibrationFallbackLevel.Global => options.GlobalSpecificity,
            _ => 0m
        };
        var recency = input.EvidenceAgeDays.HasValue
            ? Clamp01((decimal)Math.Exp(
                -Math.Log(2d) * (double)Math.Max(0m, input.EvidenceAgeDays.Value)
                / (double)options.RecencyHalfLifeDays))
            : 0m;
        var error = input.CalibrationError.HasValue
            ? Clamp01(1m - input.CalibrationError.Value / options.MaximumAcceptableCalibrationError)
            : 0m;
        var quality = input.DataQualityScore.HasValue
            ? Clamp01(input.DataQualityScore.Value)
            : 0m;

        var totalWeight = options.SampleWeight
            + options.SpecificityWeight
            + options.RecencyWeight
            + options.CalibrationErrorWeight
            + options.DataQualityWeight;
        var reliability = (
            options.SampleWeight * sample
            + options.SpecificityWeight * specificity
            + options.RecencyWeight * recency
            + options.CalibrationErrorWeight * error
            + options.DataQualityWeight * quality) / totalWeight;
        var interval = ResolveInterval(input, options);

        return new CalibrationReliabilityResult(
            Clamp01(reliability),
            sample,
            Clamp01(specificity),
            recency,
            error,
            quality,
            input.RawProbability,
            input.CalibratedProbability,
            interval.LowerBound,
            interval.UpperBound,
            input.EffectiveN ?? 0m,
            input.ExactMarketN,
            input.FamilyN,
            input.GlobalN,
            input.FallbackLevel,
            input.Version,
            input.PriorWeight,
            interval.Method,
            interval.ConfidenceLevel);
    }

    private static CalibrationInterval ResolveInterval(
        CalibrationReliabilityInput input,
        CalibrationReliabilityOptions options)
    {
        if (input.LowerBound.HasValue && input.UpperBound.HasValue)
        {
            return new CalibrationInterval(
                input.LowerBound,
                input.UpperBound,
                string.IsNullOrWhiteSpace(input.IntervalMethod)
                    ? "ExistingCalibrator"
                    : input.IntervalMethod.Trim(),
                input.ConfidenceLevel);
        }

        if (input.EffectiveN is not > 0m)
        {
            return new CalibrationInterval(null, null, "Unavailable", null);
        }

        // Wilson over EffectiveN is a conservative, deterministic adapter when
        // the upstream calibrator has sample metadata but omitted its interval.
        // It does not refit or inspect outcomes from the evaluation period.
        var n = input.EffectiveN.Value;
        var probability = input.CalibratedProbability;
        var z = options.FallbackIntervalZScore;
        var zSquared = z * z;
        var denominator = 1m + zSquared / n;
        var center = (probability + zSquared / (2m * n)) / denominator;
        var variance = probability * (1m - probability) / n
            + zSquared / (4m * n * n);
        var halfWidth = z * (decimal)Math.Sqrt((double)Math.Max(0m, variance)) / denominator;
        return new CalibrationInterval(
            Clamp01(center - halfWidth),
            Clamp01(center + halfWidth),
            "WilsonEffectiveN",
            options.FallbackIntervalConfidenceLevel);
    }

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    private static void Validate(
        CalibrationReliabilityInput input,
        CalibrationReliabilityOptions options)
    {
        if (input.RawProbability is < 0m or > 1m
            || input.CalibratedProbability is < 0m or > 1m
            || input.LowerBound is < 0m or > 1m
            || input.UpperBound is < 0m or > 1m
            || (input.LowerBound.HasValue && input.UpperBound.HasValue
                && input.LowerBound > input.UpperBound)
            || input.EffectiveN < 0m
            || input.PriorWeight < 0m
            || input.ConfidenceLevel is < 0m or > 1m
            || input.ExactMarketN < 0
            || input.FamilyN < 0
            || input.GlobalN < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Invalid calibration metadata.");
        }

        var totalWeight = options.SampleWeight
            + options.SpecificityWeight
            + options.RecencyWeight
            + options.CalibrationErrorWeight
            + options.DataQualityWeight;
        if (options.TargetEffectiveN <= 0m
            || options.RecencyHalfLifeDays <= 0m
            || options.MaximumAcceptableCalibrationError <= 0m
            || options.FallbackIntervalZScore <= 0m
            || options.FallbackIntervalConfidenceLevel is <= 0m or >= 1m
            || totalWeight <= 0m
            || options.SampleWeight < 0m
            || options.SpecificityWeight < 0m
            || options.RecencyWeight < 0m
            || options.CalibrationErrorWeight < 0m
            || options.DataQualityWeight < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid calibration reliability options.");
        }
    }

    private sealed record CalibrationInterval(
        decimal? LowerBound,
        decimal? UpperBound,
        string Method,
        decimal? ConfidenceLevel);
}
