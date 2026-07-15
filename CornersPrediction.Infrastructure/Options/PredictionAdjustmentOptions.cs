namespace CornersPrediction.Infrastructure.Options;

public sealed class PredictionAdjustmentOptions
{
    public const string SectionName = "PredictionAdjustments";

    public bool EnableRankingAdjustment { get; init; } = true;

    public double HomeRankingMaxImpactPct { get; init; } = 0.06;

    public double AwayRankingMaxImpactPct { get; init; } = 0.05;
}
