using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Predictions;

/// <summary>
/// Prediction value returned by the ML model.
/// </summary>
public sealed class PredictionResult
{
    private const double TotalCornersMae = 2.633;
    private const double TotalCornersRmse = 2.633;

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

    [JsonPropertyName("predTotalDirect")]
    public double? PredTotalDirect { get; init; }

    [JsonPropertyName("rawTotalCornersPrediction")]
    public double? RawTotalCornersPrediction => PredTotalDirect;

    [JsonPropertyName("predHomeCorners")]
    public double? PredHomeCorners { get; init; }

    [JsonPropertyName("homeCornersPrediction")]
    public double? HomeCornersPrediction => PredHomeCorners;

    [JsonPropertyName("predAwayCorners")]
    public double? PredAwayCorners { get; init; }

    [JsonPropertyName("awayCornersPrediction")]
    public double? AwayCornersPrediction => PredAwayCorners;

    [JsonPropertyName("predTotalCombined")]
    public double? PredTotalCombined { get; init; }

    [JsonPropertyName("predFinal")]
    public double? PredFinal { get; init; }

    [JsonPropertyName("finalCornersPrediction")]
    public double? FinalCornersPrediction => PredFinal ?? PredictedTotalCorners;

    [JsonPropertyName("predFinalRounded")]
    public double? PredFinalRounded { get; init; }

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

    [JsonPropertyName("rangeLow")]
    public double RangeLow => ProbableRangeLow;

    [JsonPropertyName("rangeHigh")]
    public double RangeHigh => ProbableRangeHigh;

    [JsonPropertyName("bettingLine")]
    public double? BettingLine { get; init; }

    [JsonPropertyName("recommendedSide")]
    public string RecommendedSide { get; init; } = "N/A";

    [JsonPropertyName("distanceToLine")]
    public double? DistanceToLine { get; init; }

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "N/A";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

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

    [JsonPropertyName("baseHomeCorners")]
    public double? BaseHomeCorners { get; init; }

    [JsonPropertyName("baseAwayCorners")]
    public double? BaseAwayCorners { get; init; }

    [JsonPropertyName("baseTotalCorners")]
    public double? BaseTotalCorners { get; init; }

    [JsonPropertyName("rankingAdjustment")]
    public RankingAdjustmentResult? RankingAdjustment { get; init; }

    [JsonPropertyName("finalHomeCorners")]
    public double? FinalHomeCorners { get; init; }

    [JsonPropertyName("finalAwayCorners")]
    public double? FinalAwayCorners { get; init; }

    [JsonPropertyName("finalTotalCorners")]
    public double? FinalTotalCorners { get; init; }

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

    public static PredictionResult CreateEnsemble(
        double predTotalDirect,
        double predHomeCorners,
        double predAwayCorners,
        double predTotalCombined,
        double predFinal,
        double predFinalRounded,
        double rangeLow,
        double rangeHigh,
        double? bettingLine,
        string recommendedSide,
        double? distanceToLine,
        string confidence,
        string message)
    {
        return new PredictionResult(predFinal)
        {
            PredTotalDirect = predTotalDirect,
            PredHomeCorners = predHomeCorners,
            PredAwayCorners = predAwayCorners,
            PredTotalCombined = predTotalCombined,
            PredFinal = predFinal,
            PredFinalRounded = predFinalRounded,
            ProbableRangeLow = rangeLow,
            ProbableRangeHigh = rangeHigh,
            WideRangeLow = rangeLow,
            WideRangeHigh = rangeHigh,
            LegacyHomeCorners = predHomeCorners,
            LegacyAwayCorners = predAwayCorners,
            LegacyTotalCorners = predTotalCombined,
            ModelDifference = Math.Abs(predTotalDirect - predTotalCombined),
            ModelConsensus = Math.Abs(predTotalDirect - predTotalCombined) <= 1.0
                ? "High"
                : Math.Abs(predTotalDirect - predTotalCombined) <= 2.0
                    ? "Medium"
                    : "Low",
            BettingLine = bettingLine,
            RecommendedSide = recommendedSide,
            DistanceToLine = distanceToLine,
            Confidence = confidence,
            Message = message
        };
    }
}

public sealed class RankingAdjustmentResult
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("homeRankingPosition")]
    public int? HomeRankingPosition { get; init; }

    [JsonPropertyName("awayRankingPosition")]
    public int? AwayRankingPosition { get; init; }

    [JsonPropertyName("rankingTotalTeams")]
    public int? RankingTotalTeams { get; init; }

    [JsonPropertyName("rankingSource")]
    public string? RankingSource { get; init; }

    [JsonPropertyName("rankingSeason")]
    public string? RankingSeason { get; init; }

    [JsonPropertyName("homeRankingStrength")]
    public double? HomeRankingStrength { get; init; }

    [JsonPropertyName("awayRankingStrength")]
    public double? AwayRankingStrength { get; init; }

    [JsonPropertyName("rankingStrengthDiff")]
    public double? RankingStrengthDiff { get; init; }

    [JsonPropertyName("homeAdjustmentPct")]
    public double? HomeAdjustmentPct { get; init; }

    [JsonPropertyName("awayAdjustmentPct")]
    public double? AwayAdjustmentPct { get; init; }
}
