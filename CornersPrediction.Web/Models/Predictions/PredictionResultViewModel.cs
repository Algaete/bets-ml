using System.Text.Json.Serialization;

namespace CornersPrediction.Web.Models.Predictions;

public sealed class PredictionResultViewModel
{
    [JsonPropertyName("predictedTotalCorners")]
    public double PredictedTotalCorners { get; init; }

    [JsonPropertyName("predTotalDirect")]
    public double? PredTotalDirect { get; init; }

    [JsonPropertyName("predHomeCorners")]
    public double? PredHomeCorners { get; init; }

    [JsonPropertyName("predAwayCorners")]
    public double? PredAwayCorners { get; init; }

    [JsonPropertyName("predTotalCombined")]
    public double? PredTotalCombined { get; init; }

    [JsonPropertyName("predFinal")]
    public double? PredFinal { get; init; }

    [JsonPropertyName("predFinalRounded")]
    public double? PredFinalRounded { get; init; }

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

    [JsonPropertyName("rangeLow")]
    public double? RangeLow { get; init; }

    [JsonPropertyName("rangeHigh")]
    public double? RangeHigh { get; init; }

    [JsonPropertyName("bettingLine")]
    public double? BettingLine { get; init; }

    [JsonPropertyName("recommendedSide")]
    public string? RecommendedSide { get; init; }

    [JsonPropertyName("distanceToLine")]
    public double? DistanceToLine { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("legacyHomeCorners")]
    public double? LegacyHomeCorners { get; init; }

    [JsonPropertyName("legacyAwayCorners")]
    public double? LegacyAwayCorners { get; init; }

    [JsonPropertyName("legacyTotalCorners")]
    public double? LegacyTotalCorners { get; init; }

    [JsonPropertyName("modelDifference")]
    public double? ModelDifference { get; init; }

    [JsonPropertyName("modelConsensus")]
    public string? ModelConsensus { get; init; }
}
