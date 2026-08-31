using CornersPrediction.Domain.Automation.BotG;

namespace CornersPrediction.Application.Automation.BotG;

public sealed class BotGConservativeUncertaintyService : IBotGUncertaintyService
{
    public BotGUncertaintyResult Estimate(BotGUncertaintyInput input, BotGConfiguration configuration)
    {
        var config = BotGConfiguration.Validate(configuration).Uncertainty;
        if (!double.IsFinite(input.FinalProbability) || input.FinalProbability < 0d || input.FinalProbability > 1d)
            throw new ArgumentOutOfRangeException(nameof(input.FinalProbability));
        if (!double.IsFinite(input.EnsembleDispersion) || input.EnsembleDispersion < 0d)
            throw new ArgumentOutOfRangeException(nameof(input.EnsembleDispersion));
        if (!double.IsFinite(input.CalibrationEffectiveSampleSize) || input.CalibrationEffectiveSampleSize < 0d)
            throw new ArgumentOutOfRangeException(nameof(input.CalibrationEffectiveSampleSize));

        var effectiveN = Math.Max(1d, input.CalibrationEffectiveSampleSize);
        var samplingError = Math.Sqrt(input.FinalProbability * (1d - input.FinalProbability) / effectiveN);
        var combined = Math.Sqrt(
            input.EnsembleDispersion * input.EnsembleDispersion
            + samplingError * samplingError);
        var uncertainty = Math.Clamp(combined, config.MinimumUncertainty, config.MaximumUncertainty);
        var lower = Math.Clamp(input.FinalProbability - config.ConfidenceZScore * uncertainty, 0d, input.FinalProbability);
        var upper = Math.Clamp(input.FinalProbability + config.ConfidenceZScore * uncertainty, input.FinalProbability, 1d);
        var conservative = config.UseLowerBound
            ? lower
            : Math.Clamp(input.FinalProbability - config.ConservativeLambda * uncertainty, 0d, input.FinalProbability);
        return new BotGUncertaintyResult(
            input.FinalProbability,
            lower,
            upper,
            uncertainty,
            conservative,
            config.Version);
    }
}

public static class BotGConservativeMetrics
{
    public static double Edge(double probability, double marketNoVigProbability)
    {
        if (!double.IsFinite(probability) || probability is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(probability));
        if (!double.IsFinite(marketNoVigProbability) || marketNoVigProbability is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(marketNoVigProbability));
        return probability - marketNoVigProbability;
    }

    public static double ConservativeEdge(BotGUncertaintyResult uncertainty, double marketNoVigProbability) =>
        Edge(uncertainty.ConservativeProbability, marketNoVigProbability);
}
