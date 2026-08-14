namespace CornersPrediction.Web.Models.BotAutomation;

public sealed class BotAutomationIndexViewModel
{
    public IReadOnlyList<RecommendationBotDefinitionViewModel> Bots { get; init; } = [];
    public IReadOnlyList<RecommendationJobViewModel> Jobs { get; init; } = [];
    public DateOnly DefaultDateFrom { get; init; }
    public DateOnly DefaultDateTo { get; init; }
    public string? LoadError { get; init; }
}

public sealed record RecommendationBotDefinitionViewModel
{
    public string BotKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string BaseStrategy { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsBuiltIn { get; init; }
    public IReadOnlyList<string> MarketFamilies { get; init; } = [];
    public double? MinEdge { get; init; }
    public double? MinExpectedValue { get; init; }
    public double? MinDistanceToLine { get; init; }
    public double? MaxContextDifference { get; init; }
    public bool? AllowModelDisagreement { get; init; }
    public double? MinOddsExclusive { get; init; }
    public double? MinProbabilityLiftOverImplied { get; init; }
    public decimal? StakeMultiplier { get; init; }
    public string? StrategyConfigurationJson { get; init; }
    public BotCStrategyManifestViewModel? StrategyManifest { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public bool UsesMachineLearning { get; init; }
}

public sealed record SaveRecommendationBotDefinitionViewModel
{
    public bool IsNew { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string BaseStrategy { get; init; } = "MODELS_2026";
    public bool IsEnabled { get; init; } = true;
    public IReadOnlyCollection<string>? MarketFamilies { get; init; }
    public double? MinEdge { get; init; }
    public double? MinExpectedValue { get; init; }
    public double? MinDistanceToLine { get; init; }
    public double? MaxContextDifference { get; init; }
    public bool? AllowModelDisagreement { get; init; }
    public double? MinOddsExclusive { get; init; }
    public double? MinProbabilityLiftOverImplied { get; init; }
    public decimal? StakeMultiplier { get; init; }
    public string? StrategyConfigurationJson { get; init; }
}

public sealed record BotCStrategyManifestViewModel
{
    public string StrategyName { get; init; } = string.Empty;
    public string DecisionEngineType { get; init; } = string.Empty;
    public string ProbabilityPolicy { get; init; } = string.Empty;
    public BotCStrategyConfigurationViewModel Configuration { get; init; } = new();
    public IReadOnlyList<string> SupportedMarkets { get; init; } = [];
    public IReadOnlyList<string> Pipeline { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FeatureGroups { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<string> ApprovalRules { get; init; } = [];
    public IReadOnlyList<string> DataLeakageGuards { get; init; } = [];
    public IReadOnlyList<string> PersistenceAndSettlement { get; init; } = [];
}

public sealed record BotCStrategyConfigurationViewModel
{
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string BasePredictionSource { get; init; } = string.Empty;
    public string? BaseModelVersionOverride { get; init; }
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public bool SelectorEnabled { get; init; }
    public bool AllowRuleBasedFallback { get; init; }
    public double DecayFactor { get; init; }
    public double ShrinkageStrength { get; init; }
    public int RequiredVenueMatches { get; init; }
    public double MaximumVenueWeight { get; init; }
    public double MinimumStandardDeviation { get; init; }
    public int MinimumHistoricalMatches { get; init; }
    public double MinimumCalibratedProbability { get; init; }
    public double MinimumFinalEdge { get; init; }
    public double MinimumFinalExpectedValue { get; init; }
    public double MinimumDataQualityScore { get; init; }
    public double MinimumContextAgreementScore { get; init; }
    public double MinimumRuleBasedConfidenceScore { get; init; }
    public double MaximumBaseContextDistanceSigma { get; init; }
    public double MinimumOdds { get; init; }
    public double MaximumOdds { get; init; }
    public double CalibrationIntercept { get; init; }
    public double CalibrationSlope { get; init; }
    public IReadOnlyDictionary<string, BotCCalibrationProfileViewModel> CalibrationProfiles { get; init; } =
        new Dictionary<string, BotCCalibrationProfileViewModel>();
    public double WeightCalibratedProbability { get; init; }
    public double WeightEdge { get; init; }
    public double WeightExpectedValue { get; init; }
    public double WeightExactLineHitRate { get; init; }
    public double WeightContextLineDistance { get; init; }
    public double WeightContextAgreement { get; init; }
    public double WeightDataQuality { get; init; }
    public double QualityOverallSampleWeight { get; init; }
    public double QualityVenueSampleWeight { get; init; }
    public double QualityFreshnessWeight { get; init; }
    public double QualityFeatureCompletenessWeight { get; init; }
    public double QualityMarketDataWeight { get; init; }
    public double QualityConsistencyWeight { get; init; }
    public IReadOnlyDictionary<string, BotCMarketThresholdConfigurationViewModel> MarketThresholds { get; init; } =
        new Dictionary<string, BotCMarketThresholdConfigurationViewModel>();
    public BotDTeamStrengthConfigurationViewModel TeamStrength { get; init; } = new();
    public BotEEmpiricalCalibrationConfigurationViewModel EmpiricalCalibration { get; init; } = new();
}

public sealed record BotDTeamStrengthConfigurationViewModel
{
    public bool Enabled { get; init; }
    public string Version { get; init; } = string.Empty;
    public double ResultDecayFactor { get; init; }
    public double EloKFactor { get; init; }
    public double HomeAdvantageElo { get; init; }
    public double EloWeight { get; init; }
    public double DirectMatchWeight { get; init; }
    public double CommonOpponentWeight { get; init; }
    public int MinimumMatchesPerTeam { get; init; }
    public int MinimumCommonOpponents { get; init; }
    public double MinimumConfidenceScore { get; init; }
    public double MaximumProbabilityAdjustment { get; init; }
    public double ContextExpectedValueSigmaWeight { get; init; }
    public double HomeTeamMarketWeight { get; init; }
    public double AwayTeamMarketWeight { get; init; }
    public double TotalMarketWeight { get; init; }
}

public sealed record BotEEmpiricalCalibrationConfigurationViewModel
{
    public bool Enabled { get; init; }
    public string Version { get; init; } = string.Empty;
    public string SourceBotKey { get; init; } = string.Empty;
    public int MinimumObservations { get; init; }
    public int MinimumExactMarketObservations { get; init; }
    public int MinimumEffectiveObservations { get; init; }
    public int TargetEffectiveObservations { get; init; }
    public int OutcomeAvailabilityLagHours { get; init; }
    public double ProbabilityBandwidth { get; init; }
    public double GlobalPriorStrength { get; init; }
    public double FamilyPriorStrength { get; init; }
    public double ExactMarketPriorStrength { get; init; }
    public double RecencyHalfLifeDays { get; init; }
    public double QualityWeightFloor { get; init; }
    public double MinimumReliability { get; init; }
    public double ConfidenceZScore { get; init; }
    public bool RequireSameBaseModelVersion { get; init; }
    public bool RequireNoVigProbability { get; init; }
}

public sealed record BotCCalibrationProfileViewModel
{
    public string ModelName { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public double Intercept { get; init; }
    public double Slope { get; init; }
    public int TrainingSampleCount { get; init; }
    public DateTime? TrainedThroughUtc { get; init; }
}

public sealed record BotCMarketThresholdConfigurationViewModel
{
    public bool? Enabled { get; init; }
    public double? MinimumFinalProbability { get; init; }
    public double? MinimumFinalEdge { get; init; }
    public double? MinimumFinalExpectedValue { get; init; }
    public double? MinimumDataQualityScore { get; init; }
    public double? MinimumContextAgreementScore { get; init; }
    public int? MinimumHistoricalMatches { get; init; }
    public double? MinimumOdds { get; init; }
    public double? MaximumOdds { get; init; }
}

public sealed record CreateRecommendationJobViewModel
{
    public DateOnly DateFrom { get; init; }
    public DateOnly DateTo { get; init; }
    public string? Name { get; init; }
    public IReadOnlyCollection<string>? BotKeys { get; init; }
    public IReadOnlyCollection<string>? MarketFamilies { get; init; }
    public string Mode { get; init; } = "HistoricalBackfill";
    public int BatchSize { get; init; } = 25;
    public int MaxAttempts { get; init; } = 3;
}

public sealed record RecommendationJobViewModel
{
    public Guid RecommendationJobId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public DateOnly DateFrom { get; init; }
    public DateOnly DateTo { get; init; }
    public IReadOnlyList<string> BotKeys { get; init; } = [];
    public IReadOnlyList<string> MarketFamilies { get; init; } = [];
    public int BatchSize { get; init; }
    public int NextBatchNumber { get; init; }
    public int? TotalBatches { get; init; }
    public int ProcessedBatches { get; init; }
    public int SelectedMatches { get; init; }
    public int InsertedRows { get; init; }
    public int UpdatedRows { get; init; }
    public int SkippedMatches { get; init; }
    public int ErrorMatches { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public Guid? LastRunId { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}
