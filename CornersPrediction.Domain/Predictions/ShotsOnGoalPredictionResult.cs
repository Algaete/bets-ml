using System.Text.Json;
using System.Text.Json.Serialization;

namespace CornersPrediction.Domain.Predictions;

public sealed class ShotsOnGoalPredictionResult
{
    private const double ShotsOnGoalMae = 2.6060;
    private const double ShotsOnGoalRmse = 3.2945;

    public ShotsOnGoalPredictionResult(double predictedShotsOnGoal)
        : this(null, new MarketPredictionResult(null, predictedShotsOnGoal, null, null, null, null, null, null, null, null, predictedShotsOnGoal), null)
    {
    }

    public ShotsOnGoalPredictionResult(MarketPredictionResult? shots, MarketPredictionResult sog, MarketPredictionResult? goals = null, JsonElement? debug = null)
    {
        Shots = shots;
        Sog = sog;
        Goals = goals;
        Markets = new MultiMarketPredictionResult(shots, sog, goals);
        Debug = debug;
        PredictedShots = shots?.Prediction;
        PredictedShotsOnGoal = sog.Prediction;
        PredictedGoals = goals?.Prediction;
        Mae = ShotsOnGoalMae;
        Rmse = ShotsOnGoalRmse;
        ProbableRangeLow = Math.Max(0, PredictedShotsOnGoal - Mae);
        ProbableRangeHigh = PredictedShotsOnGoal + Mae;
        WideRangeLow = Math.Max(0, PredictedShotsOnGoal - Rmse);
        WideRangeHigh = PredictedShotsOnGoal + Rmse;
    }

    [JsonPropertyName("shots")]
    public MarketPredictionResult? Shots { get; }

    [JsonPropertyName("sog")]
    public MarketPredictionResult Sog { get; }

    [JsonPropertyName("goals")]
    public MarketPredictionResult? Goals { get; }

    [JsonPropertyName("markets")]
    public MultiMarketPredictionResult Markets { get; }

    [JsonPropertyName("debug")]
    public JsonElement? Debug { get; }

    [JsonPropertyName("predictedShots")]
    public double? PredictedShots { get; }

    [JsonPropertyName("rawTotalShotsPrediction")]
    public double? RawTotalShotsPrediction => Shots?.TotalDirectPrediction;

    [JsonPropertyName("homeShotsPrediction")]
    public double? HomeShotsPrediction => Shots?.HomePrediction;

    [JsonPropertyName("awayShotsPrediction")]
    public double? AwayShotsPrediction => Shots?.AwayPrediction;

    [JsonPropertyName("finalShotsPrediction")]
    public double? FinalShotsPrediction => Shots?.FinalPrediction;

    [JsonPropertyName("predictedShotsOnGoal")]
    public double PredictedShotsOnGoal { get; }

    [JsonPropertyName("predictedGoals")]
    public double? PredictedGoals { get; }

    [JsonPropertyName("rawTotalSogPrediction")]
    public double? RawTotalSogPrediction => Sog.TotalDirectPrediction;

    [JsonPropertyName("homeSogPrediction")]
    public double? HomeSogPrediction => Sog.HomePrediction;

    [JsonPropertyName("awaySogPrediction")]
    public double? AwaySogPrediction => Sog.AwayPrediction;

    [JsonPropertyName("finalSogPrediction")]
    public double FinalSogPrediction => Sog.FinalPrediction;

    [JsonPropertyName("rawTotalGoalsPrediction")]
    public double? RawTotalGoalsPrediction => Goals?.TotalDirectPrediction;

    [JsonPropertyName("homeGoalsPrediction")]
    public double? HomeGoalsPrediction => Goals?.HomePrediction;

    [JsonPropertyName("awayGoalsPrediction")]
    public double? AwayGoalsPrediction => Goals?.AwayPrediction;

    [JsonPropertyName("finalGoalsPrediction")]
    public double? FinalGoalsPrediction => Goals?.FinalPrediction;

    [JsonPropertyName("mae")]
    public double Mae { get; }

    [JsonPropertyName("rmse")]
    public double Rmse { get; }

    [JsonPropertyName("probableRangeLow")]
    public double ProbableRangeLow { get; }

    [JsonPropertyName("probableRangeHigh")]
    public double ProbableRangeHigh { get; }

    [JsonPropertyName("wideRangeLow")]
    public double WideRangeLow { get; }

    [JsonPropertyName("wideRangeHigh")]
    public double WideRangeHigh { get; }
}

public sealed class MultiMarketPredictionResult
{
    public MultiMarketPredictionResult(
        MarketPredictionResult? shots,
        MarketPredictionResult sog,
        MarketPredictionResult? goals)
    {
        Shots = shots;
        Sog = sog;
        Goals = goals;
    }

    [JsonPropertyName("shots")]
    public MarketPredictionResult? Shots { get; }

    [JsonPropertyName("sog")]
    public MarketPredictionResult Sog { get; }

    [JsonPropertyName("goals")]
    public MarketPredictionResult? Goals { get; }
}

public sealed class MarketPredictionResult
{
    public MarketPredictionResult(
        double? line,
        double prediction,
        string? recommendation,
        string? confidence,
        double? distance,
        double? historicalAccuracy,
        double? homePrediction,
        double? awayPrediction,
        double? totalDirectPrediction,
        double? combinedHomeAwayPrediction,
        double finalPrediction,
        double? rawPrediction = null,
        bool sanityAdjusted = false,
        string? sanityReason = null,
        double? featurePrior = null)
    {
        Line = line;
        Prediction = prediction;
        RawPrediction = rawPrediction;
        SanityAdjusted = sanityAdjusted;
        SanityReason = sanityReason;
        FeaturePrior = featurePrior;
        Recommendation = recommendation;
        Confidence = confidence;
        Distance = distance;
        HistoricalAccuracy = historicalAccuracy;
        HomePrediction = homePrediction;
        AwayPrediction = awayPrediction;
        TotalDirectPrediction = totalDirectPrediction;
        CombinedHomeAwayPrediction = combinedHomeAwayPrediction;
        FinalPrediction = finalPrediction;
    }

    [JsonPropertyName("line")]
    public double? Line { get; }

    [JsonPropertyName("prediction")]
    public double Prediction { get; }

    [JsonPropertyName("rawPrediction")]
    public double? RawPrediction { get; }

    [JsonPropertyName("sanityAdjusted")]
    public bool SanityAdjusted { get; }

    [JsonPropertyName("sanityReason")]
    public string? SanityReason { get; }

    [JsonPropertyName("featurePrior")]
    public double? FeaturePrior { get; }

    [JsonPropertyName("recommendation")]
    public string? Recommendation { get; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; }

    [JsonPropertyName("distance")]
    public double? Distance { get; }

    [JsonPropertyName("historicalAccuracy")]
    public double? HistoricalAccuracy { get; }

    [JsonPropertyName("homePrediction")]
    public double? HomePrediction { get; }

    [JsonPropertyName("awayPrediction")]
    public double? AwayPrediction { get; }

    [JsonPropertyName("totalDirectPrediction")]
    public double? TotalDirectPrediction { get; }

    [JsonPropertyName("combinedHomeAwayPrediction")]
    public double? CombinedHomeAwayPrediction { get; }

    [JsonPropertyName("finalPrediction")]
    public double FinalPrediction { get; }
}
