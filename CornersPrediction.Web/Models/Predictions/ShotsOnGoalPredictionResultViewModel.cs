using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPrediction.Web.Models.Predictions;

public sealed class ShotsOnGoalPredictionResultViewModel
{
    [JsonPropertyName("shots")]
    public MarketPredictionViewModel? Shots { get; init; }

    [JsonPropertyName("sog")]
    public MarketPredictionViewModel? Sog { get; init; }

    [JsonPropertyName("goals")]
    public MarketPredictionViewModel? Goals { get; init; }

    [JsonPropertyName("markets")]
    public MultiMarketPredictionViewModel? Markets { get; init; }

    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; init; }

    [JsonPropertyName("predictedShots")]
    public double? PredictedShots { get; init; }

    [JsonPropertyName("rawTotalShotsPrediction")]
    public double? RawTotalShotsPrediction { get; init; }

    [JsonPropertyName("homeShotsPrediction")]
    public double? HomeShotsPrediction { get; init; }

    [JsonPropertyName("awayShotsPrediction")]
    public double? AwayShotsPrediction { get; init; }

    [JsonPropertyName("finalShotsPrediction")]
    public double? FinalShotsPrediction { get; init; }

    [JsonPropertyName("predictedShotsOnGoal")]
    public double PredictedShotsOnGoal { get; init; }

    [JsonPropertyName("predictedGoals")]
    public double? PredictedGoals { get; init; }

    [JsonPropertyName("rawTotalSogPrediction")]
    public double? RawTotalSogPrediction { get; init; }

    [JsonPropertyName("homeSogPrediction")]
    public double? HomeSogPrediction { get; init; }

    [JsonPropertyName("awaySogPrediction")]
    public double? AwaySogPrediction { get; init; }

    [JsonPropertyName("finalSogPrediction")]
    public double? FinalSogPrediction { get; init; }

    [JsonPropertyName("rawTotalGoalsPrediction")]
    public double? RawTotalGoalsPrediction { get; init; }

    [JsonPropertyName("homeGoalsPrediction")]
    public double? HomeGoalsPrediction { get; init; }

    [JsonPropertyName("awayGoalsPrediction")]
    public double? AwayGoalsPrediction { get; init; }

    [JsonPropertyName("finalGoalsPrediction")]
    public double? FinalGoalsPrediction { get; init; }

    [JsonPropertyName("mae")]
    public double Mae { get; init; }

    [JsonPropertyName("rmse")]
    public double Rmse { get; init; }

    [JsonPropertyName("probableRangeLow")]
    public double ProbableRangeLow { get; init; }

    [JsonPropertyName("probableRangeHigh")]
    public double ProbableRangeHigh { get; init; }

    [JsonPropertyName("wideRangeLow")]
    public double WideRangeLow { get; init; }

    [JsonPropertyName("wideRangeHigh")]
    public double WideRangeHigh { get; init; }
}

public sealed class MultiMarketPredictionViewModel
{
    [JsonPropertyName("shots")]
    public MarketPredictionViewModel? Shots { get; init; }

    [JsonPropertyName("sog")]
    public MarketPredictionViewModel? Sog { get; init; }

    [JsonPropertyName("goals")]
    public MarketPredictionViewModel? Goals { get; init; }
}

public sealed class MarketPredictionViewModel
{
    [JsonPropertyName("line")]
    public double? Line { get; init; }

    [JsonPropertyName("prediction")]
    public double Prediction { get; init; }

    [JsonPropertyName("rawPrediction")]
    public double? RawPrediction { get; init; }

    [JsonPropertyName("sanityAdjusted")]
    public bool SanityAdjusted { get; init; }

    [JsonPropertyName("sanityReason")]
    public string? SanityReason { get; init; }

    [JsonPropertyName("featurePrior")]
    public double? FeaturePrior { get; init; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("distance")]
    public double? Distance { get; init; }

    [JsonPropertyName("historicalAccuracy")]
    public double? HistoricalAccuracy { get; init; }

    [JsonPropertyName("homePrediction")]
    public double? HomePrediction { get; init; }

    [JsonPropertyName("awayPrediction")]
    public double? AwayPrediction { get; init; }

    [JsonPropertyName("totalDirectPrediction")]
    public double? TotalDirectPrediction { get; init; }

    [JsonPropertyName("combinedHomeAwayPrediction")]
    public double? CombinedHomeAwayPrediction { get; init; }

    [JsonPropertyName("finalPrediction")]
    public double FinalPrediction { get; init; }
}
