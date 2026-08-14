using System.Globalization;
using System.Text.Json.Serialization;
using CornersPrediction.Application.Automation.BotC;

namespace AutomatedCornersBot.Api;

public sealed class AutomatedBotOptions
{
    public string? SqlConnectionString { get; set; }
    public PredictionApiOptions PredictionApi { get; set; } = new();
    public string AutomationVersion { get; set; } = "AutomatedCornersBotV1.0";
    public decimal DefaultStake { get; set; } = 1m;
    public double MinEdge { get; set; } = 0.035;
    public double MinExpectedValue { get; set; } = 0.03;
    public double MinDistanceToLine { get; set; } = 0.35;
    public double MaxContextDifference { get; set; } = 1.75;
    public bool AllowModelDisagreement { get; set; }
    public bool EnableBotVariants { get; set; } = true;
    public bool EnableNewGenerationBot { get; set; } = true;
    public string NewGenerationBotSuffix { get; set; } = "C2026";
    public double ConservativeMinOdds { get; set; } = 1.60;
    public double ConservativeProbabilityLift { get; set; } = 0.10;
    public double ConservativeStakeMultiplier { get; set; } = 0.50;
    public int MinimumLeadTimeMinutes { get; set; } = 10;
    public bool EnableOverUnderPrediction { get; set; }
    public int ProgressLogEveryMatches { get; set; } = 10;

    public string ResolveSqlConnectionString()
    {
        return Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? SqlConnectionString
            ?? throw new InvalidOperationException("A SQL connection string is required. Set AZURE_SQL_CONNECTION_STRING, SQL_CONNECTION_STRING or AutomatedBot:SqlConnectionString.");
    }
}

public sealed class PredictionApiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5070";
    public string? InternalApiKey { get; set; }
}

public sealed record RunAutomatedCornersRequest(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    decimal? Stake,
    double? MinEdge,
    double? MinExpectedValue,
    double? MinDistanceToLine,
    double? MaxContextDifference,
    bool DryRun = false,
    bool? AllowModelDisagreement = null,
    string? League = null,
    bool ExcludeExistingSelections = false,
    int BatchNumber = 1,
    int BatchSize = 100,
    bool? RunBotC = null,
    bool HistoricalBacktest = false,
    bool OnlyBotC = false,
    string? MarketFamilies = null,
    bool HistoricalBackfill = false,
    string? BotKeys = null);

public sealed record AutomatedOddsAvailabilityResponse(
    DateOnly DateFrom,
    DateOnly DateTo,
    int TotalOddsRows,
    int TotalMatches,
    int BatchSize,
    int TotalBatches);

public sealed record AutomatedRunResponse(
    Guid RunId,
    DateOnly DateFrom,
    DateOnly DateTo,
    int AvailableOddsRows,
    int BatchNumber,
    int BatchSize,
    int BatchStart,
    int BatchEnd,
    int TotalBatches,
    int TotalOddsRows,
    int TotalMatches,
    int SelectedMatches,
    int InsertedRows,
    int UpdatedRows,
    int SkippedMatches,
    int ErrorMatches,
    IReadOnlyList<AutomatedSelectionResult> Selections,
    IReadOnlyList<SkippedMatchResult> Skipped,
    IReadOnlyList<ErrorMatchResult> Errors);

public sealed record UpcomingOddsRecord
{
    public long PartidoProximoCuotaId { get; init; }
    public string Source { get; init; } = "Betano";
    public string? SourceMatchId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime MatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string? StandardizedLeague { get; init; }
    public string? StandardizedHomeTeam { get; init; }
    public string? StandardizedAwayTeam { get; init; }
    public string HomeTeamGender { get; init; } = "M";
    public string AwayTeamGender { get; init; } = "M";
    public string MarketType { get; init; } = "CornersTotal";
    public decimal LineValue { get; init; }
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public DateTime UpdatedAtUtc { get; init; }

    public string EffectiveLeague => string.IsNullOrWhiteSpace(StandardizedLeague) ? League : StandardizedLeague;
    public string EffectiveHomeTeam => string.IsNullOrWhiteSpace(StandardizedHomeTeam) ? HomeTeam : StandardizedHomeTeam;
    public string EffectiveAwayTeam => string.IsNullOrWhiteSpace(StandardizedAwayTeam) ? AwayTeam : StandardizedAwayTeam;
}

public sealed record MatchHistoryItemDto(
    int Id,
    string League,
    string Season,
    DateOnly MatchDate,
    bool IsKnockout,
    string HomeTeam,
    string AwayTeam,
    string? HomeFormation,
    string? AwayFormation,
    int HomeCorners,
    int AwayCorners,
    int HomeGoals,
    int AwayGoals,
    int HomeShots,
    int AwayShots,
    int HomeShotsOnGoal,
    int AwayShotsOnGoal,
    double HomePossession,
    double AwayPossession,
    int TotalCorners);

public sealed record PredictionComparisonDto(
    double EnrichedPrediction,
    double? Difference,
    string Recommendation,
    double EnrichedShotsOnGoalPrediction = 0,
    double EnrichedGoalsPrediction = 0,
    double HomeExpectedShotsOnGoal = 0,
    double AwayExpectedShotsOnGoal = 0,
    double HomeExpectedGoals = 0,
    double AwayExpectedGoals = 0,
    double EnrichedShotsPrediction = 0,
    double HomeExpectedShots = 0,
    double AwayExpectedShots = 0);

public sealed record PredictionContextDto(
    PredictionComparisonDto Comparison,
    IReadOnlyList<MatchHistoryItemDto> HomeGeneralMatches,
    IReadOnlyList<MatchHistoryItemDto> HomeAsHomeMatches,
    IReadOnlyList<MatchHistoryItemDto> AwayGeneralMatches,
    IReadOnlyList<MatchHistoryItemDto> AwayAsAwayMatches);

public sealed record TeamBi3InfoDto(
    string League,
    string Season,
    string Team,
    bool IsBig3,
    DateTime CreatedAt);

public sealed class PredictionResultDto
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

    [JsonPropertyName("bettingLine")]
    public double? BettingLine { get; init; }

    [JsonPropertyName("distanceToLine")]
    public double? DistanceToLine { get; init; }

    [JsonPropertyName("recommendedSide")]
    public string? RecommendedSide { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("legacyTotalCorners")]
    public double? LegacyTotalCorners { get; init; }

    [JsonPropertyName("modelDifference")]
    public double? ModelDifference { get; init; }

    [JsonPropertyName("modelConsensus")]
    public string? ModelConsensus { get; init; }

    [JsonPropertyName("mae")]
    public double Mae { get; init; }

    [JsonPropertyName("rmse")]
    public double Rmse { get; init; }

    public double? ProbabilitySigma { get; init; }

    public string? ModelGeneration { get; init; }

    public string? ModelVersion { get; init; }

    public string? TrainedThrough { get; init; }

    public string? FeatureSet { get; init; }

    public IReadOnlyList<string> ModelWarnings { get; init; } = Array.Empty<string>();
}

public sealed class OverUnderPredictionResultDto
{
    [JsonPropertyName("bettingLine")]
    public double BettingLine { get; init; }

    [JsonPropertyName("prediction")]
    public string? Prediction { get; init; }

    [JsonPropertyName("predictedClass")]
    public int PredictedClass { get; init; }

    [JsonPropertyName("overProbability")]
    public double? OverProbability { get; init; }

    [JsonPropertyName("underProbability")]
    public double? UnderProbability { get; init; }

    [JsonPropertyName("confidence")]
    public string? Confidence { get; init; }

    [JsonPropertyName("distanceToLine")]
    public double DistanceToLine { get; init; }
}

public sealed class MultiMarketPredictionDto
{
    [JsonPropertyName("shots")]
    public MarketPredictionDto? Shots { get; init; }

    [JsonPropertyName("sog")]
    public MarketPredictionDto? ShotsOnGoal { get; init; }

    [JsonPropertyName("goals")]
    public MarketPredictionDto? Goals { get; init; }
}

public sealed class MarketPredictionDto
{
    [JsonPropertyName("line")]
    public double? Line { get; init; }

    [JsonPropertyName("prediction")]
    public double Prediction { get; init; }

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

public sealed class AutomatedSelectionCandidate
{
    public required UpcomingOddsRecord Odds { get; init; }
    public required PredictionResultDto CornersPrediction { get; init; }
    public OverUnderPredictionResultDto? OverUnderPrediction { get; init; }
    public required PredictionContextDto PredictionContext { get; init; }
    public required Dictionary<string, object?> Features { get; init; }
    public required string SelectedSide { get; init; }
    public required decimal SelectedOdds { get; init; }
    public required double ModelProbability { get; init; }
    public required double ImpliedProbability { get; init; }
    public required double ProbabilityEdge { get; init; }
    public required double ExpectedValue { get; init; }
    public required double KellyFraction { get; init; }
    public required double DistanceToLine { get; init; }
    public required double ContextDifference { get; init; }
    public required double SelectionScore { get; init; }
    public required string DecisionReason { get; init; }
    public required string SelectionStatus { get; init; }
}

public sealed record AutomatedSelectionResult(
    string MergeAction,
    PersistedAutomatedSelection Selection);

public sealed record SkippedMatchResult(
    string League,
    string HomeTeam,
    string AwayTeam,
    DateTime MatchDate,
    string Reason);

public sealed record ErrorMatchResult(
    string League,
    string HomeTeam,
    string AwayTeam,
    DateTime MatchDate,
    string Error);

public sealed record UpsertSelectionResult(
    long SelectionId,
    string MergeAction);

public sealed record PersistedAutomatedSelection
{
    public long AutomatedCornerBetSelectionId { get; init; }
    public Guid RunId { get; init; }
    public string AutomationVersion { get; init; } = string.Empty;
    public string Source { get; init; } = "Betano";
    public string? SourceMatchId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public long? MatchHistoryId { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime MatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string? StandardizedLeague { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string? StandardizedHomeTeam { get; init; }
    public string? StandardizedAwayTeam { get; init; }
    public string HomeTeamGender { get; init; } = "M";
    public string AwayTeamGender { get; init; } = "M";
    public string SourceMarketType { get; init; } = "CornersTotal";
    public string MarketType { get; init; } = "TotalCorners";
    public decimal LineValue { get; init; }
    public string SelectedSide { get; init; } = string.Empty;
    public decimal Odds { get; init; }
    public decimal Stake { get; init; }
    public decimal? FlatStake { get; init; }
    public decimal? ImpliedProbability { get; init; }
    public decimal? ModelProbability { get; init; }
    public decimal? ProbabilityEdge { get; init; }
    public decimal? ExpectedValue { get; init; }
    public decimal? KellyFraction { get; init; }
    public decimal? SelectionScore { get; init; }
    public decimal? PredictedTotalCorners { get; init; }
    public decimal? PredTotalDirect { get; init; }
    public decimal? PredHomeCorners { get; init; }
    public decimal? PredAwayCorners { get; init; }
    public decimal? PredTotalCombined { get; init; }
    public decimal? DistanceToLine { get; init; }
    public string? ConfidenceLevel { get; init; }
    public string? OverUnderConfidenceLevel { get; init; }
    public string? ModelConsensus { get; init; }
    public decimal? ContextTotalCorners { get; init; }
    public decimal? ContextDifference { get; init; }
    public string? RecommendedSide { get; init; }
    public string Status { get; init; } = "Pending";
    public int? ActualHomeCorners { get; init; }
    public int? ActualAwayCorners { get; init; }
    public int? ActualTotalCorners { get; init; }
    public int? SettlementActualValue { get; init; }
    public decimal? SettlementFactor { get; init; }
    public string? SettlementReason { get; init; }
    public string? SettlementSource { get; init; }
    public string? SettlementMatchStatus { get; init; }
    public string? LastSettlementCheckReason { get; init; }
    public DateTime? LastSettlementCheckAtUtc { get; init; }
    public decimal? ProfitLoss { get; init; }
    public decimal? YieldPct { get; init; }
    public string? DecisionReason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
}

public sealed record BotVariantProfile(
    string Key,
    string AutomationVersion,
    string DisplayName,
    bool UsesPickSelector,
    bool UsesNewGenerationModels,
    IReadOnlySet<string> MarketFamilies,
    double MinEdge,
    double MinExpectedValue,
    double MinDistanceToLine,
    double MaxContextDifference,
    bool AllowModelDisagreement,
    double? MinOddsExclusive,
    double MinProbabilityLiftOverImplied,
    decimal StakeMultiplier,
    BotCStrategyConfiguration? SelectorConfiguration);

public sealed record PersistBotCEvaluationCommand(
    Guid RunId,
    string BotKey,
    string AutomationVersion,
    UpcomingOddsRecord Odds,
    string MarketType,
    string BaseModelName,
    string BaseModelVersion,
    BotCPickDecision Decision,
    DateTime? BaseModelTrainedThroughUtc = null,
    long? PublishedSelectionId = null);

public sealed class PersistSelectionCommand
{
    public Guid RunId { get; init; }
    public required string AutomationVersion { get; init; }
    public required UpcomingOddsRecord Odds { get; init; }
    public required string SelectedSide { get; init; }
    public required decimal SelectedOdds { get; init; }
    public required decimal Stake { get; init; }
    public required double ImpliedProbability { get; init; }
    public required double ModelProbability { get; init; }
    public required double ProbabilityEdge { get; init; }
    public required double ExpectedValue { get; init; }
    public required double KellyFraction { get; init; }
    public required double SelectionScore { get; init; }
    public required PredictionResultDto CornersPrediction { get; init; }
    public OverUnderPredictionResultDto? OverUnderPrediction { get; init; }
    public required PredictionContextDto PredictionContext { get; init; }
    public required string DecisionReason { get; init; }
}

internal static class NumericExtensions
{
    public static decimal ToSqlDecimal(this double value) =>
        Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
