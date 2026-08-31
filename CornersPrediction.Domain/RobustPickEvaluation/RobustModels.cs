namespace CornersPrediction.Domain.RobustPickEvaluation;

public sealed record PredictionComponent(
    PredictionComponentType ComponentType,
    decimal PredictedValue,
    decimal? ProbabilityForSelection,
    decimal Weight,
    bool IsUsable,
    string? SourceVersion,
    DateTime AsOfUtc,
    string? ExclusionReason,
    decimal DataQualityScore);

public sealed class PredictionConsensusRequest
{
    public required decimal Line { get; init; }
    public required SelectionSide Side { get; init; }
    public decimal? DirectPrediction { get; init; }
    public decimal? HomePrediction { get; init; }
    public decimal? AwayPrediction { get; init; }
    public decimal? ContextPrediction { get; init; }
    public decimal? ReconciledPrediction { get; init; }
    public decimal ErrorScale { get; init; }
    public decimal NormalizationEpsilon { get; init; } = 0.000001m;
    public DateTime? EvaluationAsOfUtc { get; init; }
    public IReadOnlyList<PredictionComponent> AdditionalComponents { get; init; } = [];
}

public sealed record PredictionConsensusResult(
    decimal? DirectPrediction,
    decimal? ComponentsPrediction,
    decimal? ContextPrediction,
    decimal? ReconciledPrediction,
    decimal? DirectDistance,
    decimal? ComponentsDistance,
    decimal? ContextDistance,
    decimal? ReconciledDistance,
    decimal ConsensusMinimum,
    decimal ConsensusMaximum,
    decimal ConsensusRange,
    decimal? CoherenceGap,
    decimal WorstCasePrediction,
    decimal WorstCaseDistance,
    decimal NormalizedWorstCaseDistance,
    decimal NormalizedConsensusRange,
    decimal? NormalizedCoherenceGap,
    bool SideAgreement,
    decimal MagnitudeAgreementScore,
    decimal? ProbabilityAgreementScore,
    decimal? CoherenceScore,
    IReadOnlyList<PredictionComponent> UsableComponents);

public sealed record ComponentValidationEvidence(
    PredictionComponentType ComponentType,
    decimal ValidationError,
    decimal EffectiveSampleSize,
    string Version);

public sealed class PredictionReconciliationOptions
{
    public decimal Epsilon { get; init; } = 0.000001m;
    public decimal MinimumValidationEffectiveN { get; init; } = 30m;
    public decimal TargetValidationEffectiveN { get; init; } = 150m;
    public decimal MaximumSingleSourceWeight { get; init; } = 0.80m;
    public DateTime? EvaluationAsOfUtc { get; init; }
}

public sealed record PredictionReconciliationResult(
    decimal? ReconciledPrediction,
    IReadOnlyDictionary<PredictionComponentType, decimal> Weights,
    ReconciliationFallbackReason FallbackReason,
    string ReconciliationVersion,
    decimal ValidationEffectiveN);

public sealed record HistoricalResidualObservation(
    long FixtureId,
    DateTime FixtureStartUtc,
    DateTime FixtureEndUtc,
    DateTime PredictionAsOfUtc,
    DateTime ModelTrainedThroughUtc,
    MarketFamily MarketFamily,
    string MarketType,
    MarketScope MarketScope,
    SelectionSide Side,
    string? League,
    decimal Line,
    decimal? Odds,
    decimal HistoricalPreMatchPrediction,
    decimal ActualResult,
    decimal DataQualityScore,
    string? ModelVersionFamily,
    ResidualSourceScope SourceScope,
    DateTime? OutcomeAvailableUtc = null);

public sealed class PredictiveDistributionRequest
{
    public required long FixtureId { get; init; }
    public required DateTime EvaluationAsOfUtc { get; init; }
    public required MarketFamily MarketFamily { get; init; }
    public required string MarketType { get; init; }
    public required MarketScope MarketScope { get; init; }
    public required SelectionSide Side { get; init; }
    public required decimal Line { get; init; }
    public required decimal ReconciledPrediction { get; init; }
    public decimal? Odds { get; init; }
    public string? League { get; init; }
    public required string ModelVersion { get; init; }
    public required string RobustnessVersion { get; init; }
}

public sealed class EmpiricalResidualBootstrapOptions
{
    public required TimeSpan OutcomeAvailabilityLag { get; init; }
    public int SimulationCount { get; init; } = 5000;
    public decimal ProbabilityLowerQuantile { get; init; } = 0.10m;
    public decimal ProbabilityUpperQuantile { get; init; } = 0.90m;
    public decimal MinimumEffectiveN { get; init; } = 30m;
    public decimal TargetEffectiveN { get; init; } = 150m;
    public decimal RecencyHalfLifeDays { get; init; } = 90m;
    public decimal LineBandWidth { get; init; } = 1m;
    public decimal LineSimilarityScale { get; init; } = 2m;
    public decimal OddsSimilarityScale { get; init; } = 0.5m;
    public bool UseLineSimilarity { get; init; } = true;
    public bool UseOddsSimilarity { get; init; } = true;
    public decimal SameModelVersionWeight { get; init; } = 1m;
    public decimal DifferentModelVersionWeight { get; init; } = 0.75m;
    public decimal SameLeagueWeight { get; init; } = 1m;
    public decimal DifferentLeagueWeight { get; init; } = 0.75m;
    public decimal Epsilon { get; init; } = 0.000001m;
    public decimal? ConfiguredModelMae { get; init; }
}

public sealed record PredictiveDistribution(
    decimal P01,
    decimal P05,
    decimal P10,
    decimal P25,
    decimal P50,
    decimal P75,
    decimal P90,
    decimal P95,
    decimal P99,
    decimal Mean,
    decimal StandardDeviation,
    decimal MedianAbsoluteDeviation,
    decimal? ErrorScale,
    decimal EffectiveSampleSize,
    int SimulationCount,
    string DistributionMethod,
    string DistributionVersion,
    IReadOnlyDictionary<int, int> Histogram,
    decimal PWin,
    decimal PHalfWin,
    decimal PPush,
    decimal PHalfLoss,
    decimal PLoss);

public sealed record PredictiveDistributionResult(
    PredictiveDistribution? Distribution,
    ResidualFallbackLevel FallbackLevel,
    int RawObservationCount,
    decimal EffectiveObservationCount,
    decimal MinimumRequiredEffectiveN,
    decimal TargetEffectiveN,
    ErrorScaleMethod ErrorScaleMethod,
    ResidualSourceScope? ResidualSourceScope,
    ulong DeterministicSeed,
    IReadOnlyList<RobustReasonCode> Warnings);

public sealed record AsianSettlementProbabilities(
    decimal PWin,
    decimal PHalfWin,
    decimal PPush,
    decimal PHalfLoss,
    decimal PLoss);

public sealed record AsianValueResult(
    decimal ExpectedPositiveFactor,
    decimal ExpectedNegativeFactor,
    decimal ExpectedValue,
    decimal? FairOdds,
    decimal? ModelFairProbability);

public sealed record CalibrationReliabilityInput(
    decimal RawProbability,
    decimal CalibratedProbability,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal? EffectiveN,
    int ExactMarketN,
    int FamilyN,
    int GlobalN,
    CalibrationFallbackLevel FallbackLevel,
    decimal? EvidenceAgeDays,
    decimal? CalibrationError,
    decimal? DataQualityScore,
    string Version,
    decimal? PriorWeight = null,
    string? IntervalMethod = null,
    decimal? ConfidenceLevel = null);

public sealed class CalibrationReliabilityOptions
{
    public decimal TargetEffectiveN { get; init; } = 150m;
    public decimal RecencyHalfLifeDays { get; init; } = 90m;
    public decimal MaximumAcceptableCalibrationError { get; init; } = 0.10m;
    public decimal SampleWeight { get; init; } = 0.30m;
    public decimal SpecificityWeight { get; init; } = 0.20m;
    public decimal RecencyWeight { get; init; } = 0.15m;
    public decimal CalibrationErrorWeight { get; init; } = 0.20m;
    public decimal DataQualityWeight { get; init; } = 0.15m;
    public decimal ExactMarketSpecificity { get; init; } = 1m;
    public decimal FamilySpecificity { get; init; } = 0.65m;
    public decimal GlobalSpecificity { get; init; } = 0.30m;
    /// <summary>
    /// Auditable fallback interval used only when the existing calibrator did
    /// not persist bounds. The z-score and confidence label are configuration,
    /// not learned from the evaluation period.
    /// </summary>
    public decimal FallbackIntervalZScore { get; init; } = 1.64485362695147m;
    public decimal FallbackIntervalConfidenceLevel { get; init; } = 0.90m;
}

public sealed record CalibrationReliabilityResult(
    decimal ReliabilityScore,
    decimal SampleScore,
    decimal SpecificityScore,
    decimal RecencyScore,
    decimal CalibrationErrorScore,
    decimal DataQualityScore,
    decimal RawProbability,
    decimal CalibratedProbability,
    decimal? LowerBound,
    decimal? UpperBound,
    decimal EffectiveN,
    int ExactMarketN,
    int FamilyN,
    int GlobalN,
    CalibrationFallbackLevel FallbackLevel,
    string Version,
    decimal? PriorWeight,
    string IntervalMethod,
    decimal? ConfidenceLevel);

public sealed record RobustScenarioValue(
    string ScenarioName,
    decimal ModelFairProbability,
    decimal ExpectedValue,
    bool RetainsOriginalSide,
    decimal ProbabilityWeight,
    EvidenceStatus EvidenceStatus,
    bool IsUsable);

public sealed record RobustValueEvaluationResult(
    decimal PointEdge,
    decimal PointExpectedValue,
    decimal? RobustModelFairProbability,
    decimal? RobustEdge,
    decimal? RobustExpectedValue,
    decimal? ExpectedValueP10,
    decimal? ExpectedValueP50,
    decimal? ExpectedValueP90,
    decimal? EdgeP10,
    decimal? EdgeP50,
    decimal? EdgeP90,
    decimal PositiveEvStability,
    decimal ScenarioSideStability,
    int ValidScenarioCount,
    IReadOnlyList<RobustReasonCode> Warnings);

public sealed record RobustnessComponents(
    decimal? RobustEdgeScore,
    decimal? RobustExpectedValueScore,
    decimal? PositiveEvStability,
    decimal? CalibrationReliability,
    decimal? ScenarioStability,
    decimal? ConsensusQuality,
    decimal? Coherence,
    decimal? DataQuality,
    decimal? OddsReliability);

public sealed class RiskAdjustedStakeOptions
{
    public bool AllowIncrease { get; init; }
    public decimal HighRobustnessThreshold { get; init; } = 0.90m;
    public decimal MediumRobustnessThreshold { get; init; } = 0.80m;
    public decimal MinimumRobustnessThreshold { get; init; } = 0.75m;
    public decimal HighMultiplier { get; init; } = 1m;
    public decimal MediumMultiplier { get; init; } = 0.75m;
    public decimal MinimumMultiplier { get; init; } = 0.50m;
    public decimal MaximumStake { get; init; } = decimal.MaxValue;
    public IReadOnlyDictionary<string, decimal> ComponentWeights { get; init; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(RobustnessComponents.RobustEdgeScore)] = 0.15m,
            [nameof(RobustnessComponents.RobustExpectedValueScore)] = 0.15m,
            [nameof(RobustnessComponents.PositiveEvStability)] = 0.15m,
            [nameof(RobustnessComponents.CalibrationReliability)] = 0.15m,
            [nameof(RobustnessComponents.ScenarioStability)] = 0.10m,
            [nameof(RobustnessComponents.ConsensusQuality)] = 0.10m,
            [nameof(RobustnessComponents.Coherence)] = 0.05m,
            [nameof(RobustnessComponents.DataQuality)] = 0.075m,
            [nameof(RobustnessComponents.OddsReliability)] = 0.075m
        };
}

public sealed record RiskAdjustedStakeResult(
    decimal RobustnessScore,
    decimal OriginalStake,
    decimal RecommendedStake,
    decimal StakeMultiplier,
    IReadOnlyDictionary<string, decimal> EffectiveComponents);

public sealed class RobustPickPolicyOptions
{
    public decimal MinPointEdge { get; init; }
    public decimal MinPointExpectedValue { get; init; }
    public decimal MinRobustEdge { get; init; } = 0.005m;
    public decimal MinRobustExpectedValue { get; init; }
    public decimal MinPositiveEvStability { get; init; } = 0.75m;
    public decimal MinScenarioSideStability { get; init; } = 0.75m;
    public decimal MinNormalizedWorstCaseDistance { get; init; } = 0.25m;
    public decimal MaxNormalizedConsensusRange { get; init; } = 0.75m;
    public decimal MaxNormalizedCoherenceGap { get; init; } = 0.75m;
    public decimal MinCalibrationReliability { get; init; } = 0.50m;
    public decimal MinResidualEffectiveN { get; init; } = 30m;
    public decimal MinDataQuality { get; init; } = 0.50m;
    public bool RequireSideAgreement { get; init; } = true;
    public bool RequireNoVig { get; init; }
    public bool RequireIntelligence { get; init; }
    public bool RequireCoherence { get; init; }
    public bool ManualReviewOnScenarioConflict { get; init; } = true;
}

public sealed record RobustPickPolicyInput
{
    public required EvaluationMode Mode { get; init; }
    public required CurrentSystemDecision CurrentDecision { get; init; }
    public required decimal OriginalStake { get; init; }
    public required decimal RiskAdjustedStake { get; init; }
    public required decimal RobustnessScore { get; init; }
    public bool DataIsValid { get; init; } = true;
    public bool TemporalDataIsValid { get; init; } = true;
    public bool ModelWasTrainedBeforeFixture { get; init; } = true;
    public bool MarketPriceAvailable { get; init; } = true;
    public bool OddsAreFresh { get; init; } = true;
    public NoVigStatus NoVigStatus { get; init; } = NoVigStatus.Available;
    public bool ErrorScaleAvailable { get; init; } = true;
    public decimal ResidualEffectiveN { get; init; }
    public bool SideAgreement { get; init; }
    public decimal NormalizedWorstCaseDistance { get; init; }
    public decimal NormalizedConsensusRange { get; init; }
    public decimal? NormalizedCoherenceGap { get; init; }
    public decimal CalibrationReliability { get; init; }
    public decimal PointEdge { get; init; }
    public decimal PointExpectedValue { get; init; }
    public decimal? RobustEdge { get; init; }
    public decimal? RobustExpectedValue { get; init; }
    public decimal PositiveEvStability { get; init; }
    public decimal ScenarioSideStability { get; init; }
    public decimal DataQualityScore { get; init; }
    public bool ExposureAvailable { get; init; } = true;
    public bool CorrelatedExposureAvailable { get; init; } = true;
    public bool ScenarioConflictRequiresReview { get; init; }
    public EvidenceStatus IntelligenceEvidenceStatus { get; init; } = EvidenceStatus.NotApplicable;
    public bool SnapshotExpired { get; init; }
    public bool MarketAutomationNameMatches { get; init; } = true;
}

public sealed record RobustPickEvaluationResult(
    EvaluationMode Mode,
    CurrentSystemDecision CurrentSystemDecision,
    RobustDecision RobustDecision,
    RobustDecision EffectiveDecision,
    decimal OriginalStake,
    decimal RecommendedStake,
    decimal EffectiveStake,
    decimal StakeMultiplier,
    decimal RobustnessScore,
    IReadOnlyList<RobustReasonCode> RejectionReasons,
    IReadOnlyList<RobustReasonCode> Warnings,
    string HumanReadableReason)
{
    public bool ChangesCurrentBehavior => Mode == EvaluationMode.Enforce
        && ((CurrentSystemDecision == CurrentSystemDecision.Bet) != (EffectiveDecision is RobustDecision.Approve or RobustDecision.ReduceStake)
            || EffectiveStake != OriginalStake);
}

public sealed record PortfolioPick(
    string PickKey,
    long FixtureId,
    string HomeTeamKey,
    string AwayTeamKey,
    string LeagueKey,
    MarketFamily MarketFamily,
    string BotKey,
    DateOnly Day,
    string CorrelationCluster,
    decimal RequestedStake,
    decimal RobustnessScore);

public sealed class PortfolioExposureOptions
{
    public decimal MaximumStakePerFixture { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerTeam { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerLeague { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerMarketFamily { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerBot { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerDay { get; init; } = decimal.MaxValue;
    public decimal MaximumStakePerCorrelationCluster { get; init; } = decimal.MaxValue;
    public int MaximumRelatedPicksPerFixture { get; init; } = int.MaxValue;
}

public sealed record PortfolioAllocation(
    PortfolioPick Pick,
    decimal ApprovedStake,
    bool IsRejected,
    IReadOnlyList<RobustReasonCode> ReasonCodes);

public sealed record RobustEvaluationIdentity(
    Guid Id,
    long BotPickSelectionId,
    long? BotPickId,
    long FixtureId,
    int EvaluationSequence,
    string EvaluationVersion,
    DateTime AsOfUtc,
    DateTime CreatedUtc,
    bool IsCurrent,
    Guid? SupersedesEvaluationId,
    string RobustnessVersion,
    string PolicyVersion);
