using System.Text.Json.Serialization;

namespace CornersPrediction.Web.Models.Predictions;

public sealed class OverUnderPredictionResultViewModel
{
    [JsonPropertyName("bettingLine")]
    public double BettingLine { get; init; }

    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = string.Empty;

    [JsonPropertyName("predictedClass")]
    public int PredictedClass { get; init; }

    [JsonPropertyName("overProbability")]
    public double? OverProbability { get; init; }

    [JsonPropertyName("underProbability")]
    public double? UnderProbability { get; init; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = string.Empty;

    [JsonPropertyName("distanceToLine")]
    public double DistanceToLine { get; init; }

    [JsonPropertyName("absDistanceToLine")]
    public double AbsDistanceToLine { get; init; }
}
