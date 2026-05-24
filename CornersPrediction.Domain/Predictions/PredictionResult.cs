using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Predictions;

/// <summary>
/// Prediction value returned by the ML model.
/// </summary>
public sealed class PredictionResult
{
    private const double TotalCornersMae = 2.6727;
    private const double TotalCornersRmse = 3.3707;

    public PredictionResult(double predictedTotalCorners)
    {
        PredictedTotalCorners = predictedTotalCorners;
        Mae = TotalCornersMae;
        Rmse = TotalCornersRmse;
        ProbableRangeLow = Math.Max(0, predictedTotalCorners - Mae);
        ProbableRangeHigh = predictedTotalCorners + Mae;
        WideRangeLow = Math.Max(0, predictedTotalCorners - Rmse);
        WideRangeHigh = predictedTotalCorners + Rmse;
    }

    [JsonPropertyName("predictedTotalCorners")]
    public double PredictedTotalCorners { get; init; }

    [JsonPropertyName("mae")]
    public double Mae { get; init; } = TotalCornersMae;

    [JsonPropertyName("rmse")]
    public double Rmse { get; init; } = TotalCornersRmse;

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

    public static PredictionResult Create(
        double predictedTotalCorners,
        double? legacyHomeCorners = null,
        double? legacyAwayCorners = null)
    {
        var result = new PredictionResult(predictedTotalCorners)
        {
            LegacyHomeCorners = legacyHomeCorners,
            LegacyAwayCorners = legacyAwayCorners
        };

        var legacyTotalCorners = legacyHomeCorners + legacyAwayCorners;
        if (legacyTotalCorners is null)
        {
            return result;
        }

        var difference = Math.Abs(predictedTotalCorners - legacyTotalCorners.Value);

        return new PredictionResult(predictedTotalCorners)
        {
            LegacyHomeCorners = legacyHomeCorners,
            LegacyAwayCorners = legacyAwayCorners,
            LegacyTotalCorners = legacyTotalCorners,
            ModelDifference = difference,
            ModelConsensus = difference <= 1.0
                ? "High"
                : difference <= 2.0
                    ? "Medium"
                    : "Low"
        };
    }
}
