using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Predictions;

public sealed class OverUnderPredictionResult
{
    public OverUnderPredictionResult(
        double bettingLine,
        int predictedClass,
        double? overProbability,
        double? underProbability,
        double distanceToLine,
        double absDistanceToLine)
    {
        BettingLine = bettingLine;
        PredictedClass = predictedClass;
        Prediction = predictedClass == 1 ? "OVER" : "UNDER";
        OverProbability = overProbability;
        UnderProbability = underProbability;
        DistanceToLine = distanceToLine;
        AbsDistanceToLine = absDistanceToLine;
        Confidence = CalculateConfidence(overProbability, underProbability, absDistanceToLine);
    }

    [JsonPropertyName("bettingLine")]
    public double BettingLine { get; }

    [JsonPropertyName("prediction")]
    public string Prediction { get; }

    [JsonPropertyName("predictedClass")]
    public int PredictedClass { get; }

    [JsonPropertyName("overProbability")]
    public double? OverProbability { get; }

    [JsonPropertyName("underProbability")]
    public double? UnderProbability { get; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; }

    [JsonPropertyName("distanceToLine")]
    public double DistanceToLine { get; }

    [JsonPropertyName("absDistanceToLine")]
    public double AbsDistanceToLine { get; }

    private static string CalculateConfidence(
        double? overProbability,
        double? underProbability,
        double absDistanceToLine)
    {
        if (overProbability is not null || underProbability is not null)
        {
            var maxProbability = Math.Max(overProbability ?? 0, underProbability ?? 0);

            if (maxProbability >= 0.70 && absDistanceToLine >= 1.0)
            {
                return "HIGH";
            }

            if (maxProbability >= 0.60 && absDistanceToLine >= 0.5)
            {
                return "MEDIUM";
            }

            return "LOW";
        }

        if (absDistanceToLine >= 1.5)
        {
            return "HIGH";
        }

        if (absDistanceToLine >= 0.75)
        {
            return "MEDIUM";
        }

        return "LOW";
    }
}
