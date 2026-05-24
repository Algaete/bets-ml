using System.Text.Json.Serialization;

namespace CornersPrediction.Web.Models.Predictions;

public sealed class PredictionResultViewModel
{
    [JsonPropertyName("predictedTotalCorners")]
    public double PredictedTotalCorners { get; init; }

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
