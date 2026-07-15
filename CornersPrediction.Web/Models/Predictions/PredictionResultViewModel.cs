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

    [JsonPropertyName("baseHomeCorners")]
    public double? BaseHomeCorners { get; init; }

    [JsonPropertyName("baseAwayCorners")]
    public double? BaseAwayCorners { get; init; }

    [JsonPropertyName("baseTotalCorners")]
    public double? BaseTotalCorners { get; init; }

    [JsonPropertyName("rankingAdjustment")]
    public RankingAdjustmentViewModel? RankingAdjustment { get; init; }

    [JsonPropertyName("finalHomeCorners")]
    public double? FinalHomeCorners { get; init; }

    [JsonPropertyName("finalAwayCorners")]
    public double? FinalAwayCorners { get; init; }

    [JsonPropertyName("finalTotalCorners")]
    public double? FinalTotalCorners { get; init; }
}

public sealed class RankingAdjustmentViewModel
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
