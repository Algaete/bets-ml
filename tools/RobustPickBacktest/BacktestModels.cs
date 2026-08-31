using System.Text.Json.Serialization;

namespace RobustPickBacktest;

public sealed class ResolvedEvaluation
{
    public required string EvaluationId { get; init; }
    public required string SelectionKey { get; init; }
    public required long FixtureId { get; init; }
    public required DateTimeOffset EvaluationAsOfUtc { get; init; }
    public required DateTimeOffset FixtureStartUtc { get; init; }
    public required DateTimeOffset FixtureEndUtc { get; init; }
    public DateTimeOffset? OutcomeAvailableUtc { get; init; }
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public string? MarketType { get; init; }
    public string? Scope { get; init; }
    public string? Side { get; init; }
    public string? League { get; init; }
    public decimal? LineValue { get; init; }
    public decimal? RobustnessScore { get; init; }
    public string? ExposureGroupKey { get; init; }
    public bool BaselineApproved { get; init; }
    public decimal BaselineStake { get; init; }
    public required string RobustDecision { get; init; }
    public decimal? RobustRecommendedStake { get; init; }
    public required decimal Odds { get; init; }
    public required decimal SettlementFactor { get; init; }
    public decimal? UnitProfitLoss { get; init; }
    public decimal? BaselineProbability { get; init; }
    public decimal? RobustProbability { get; init; }
    public decimal? MarketProbability { get; init; }
    public decimal? BinaryOutcome { get; init; }
    public decimal? ClosingOdds { get; init; }
    public decimal? ClosingNoVigProbability { get; init; }
    public decimal? ClvOdds { get; init; }
    public decimal? ClvProbability { get; init; }
    public decimal? ClvLine { get; init; }
    public bool ThresholdGridEligible { get; init; } = true;
    public decimal? ThresholdGridStake { get; init; }
    public decimal? PointEdge { get; init; }
    public decimal? RobustEdge { get; init; }
    public decimal? PointExpectedValue { get; init; }
    public decimal? RobustExpectedValue { get; init; }
    public decimal? PositiveEvStability { get; init; }
    public decimal? ScenarioSideStability { get; init; }
    public decimal? NormalizedWorstCaseDistance { get; init; }
    public decimal? NormalizedConsensusRange { get; init; }
    public decimal? NormalizedCoherenceGap { get; init; }
    public decimal? CalibrationReliability { get; init; }
    public decimal? ObservedMarketValue { get; init; }
    public AuditablePredictiveCdf? BaselinePredictiveCdf { get; init; }
    public AuditablePredictiveCdf? RobustPredictiveCdf { get; init; }
}

public sealed class AuditablePredictiveCdf
{
    public required string DistributionId { get; init; }
    public required string Method { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public string? SourceVersion { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public required IReadOnlyList<PredictiveCdfPoint> Points { get; init; }
}

public sealed record PredictiveCdfPoint(decimal Value, decimal CumulativeProbability);

public sealed class BacktestConfiguration
{
    public decimal TrainingWindowDays { get; init; } = 90m;
    public decimal ValidationWindowDays { get; init; } = 30m;
    public decimal TestWindowDays { get; init; } = 30m;
    public decimal StepDays { get; init; } = 30m;
    public decimal EmbargoHours { get; init; }
    public decimal OutcomeAvailabilityLagHours { get; init; } = 8m;
    public int MinimumTrainingObservations { get; init; } = 30;
    public int MinimumValidationObservations { get; init; } = 15;
    public DateTimeOffset? FirstTestStartUtc { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public bool LatestEvaluationPerSelection { get; init; } = true;
    public ClusterBootstrapConfiguration Bootstrap { get; init; } = new();
    public ThresholdGridConfiguration ThresholdGrid { get; init; } = new();
    public GroupingConfiguration Grouping { get; init; } = new();

    [JsonIgnore]
    public TimeSpan TrainingWindow => TimeSpan.FromDays((double)TrainingWindowDays);

    [JsonIgnore]
    public TimeSpan ValidationWindow => TimeSpan.FromDays((double)ValidationWindowDays);

    [JsonIgnore]
    public TimeSpan TestWindow => TimeSpan.FromDays((double)TestWindowDays);

    [JsonIgnore]
    public TimeSpan Step => TimeSpan.FromDays((double)StepDays);

    [JsonIgnore]
    public TimeSpan Embargo => TimeSpan.FromHours((double)EmbargoHours);

    [JsonIgnore]
    public TimeSpan OutcomeAvailabilityLag =>
        TimeSpan.FromHours((double)OutcomeAvailabilityLagHours);
}

public sealed class GroupingConfiguration
{
    public decimal OddsBandWidth { get; init; } = 0.25m;
    public decimal LineBandWidth { get; init; } = 0.50m;
    public decimal CalibrationReliabilityBandWidth { get; init; } = 0.10m;
    public string MissingValueLabel { get; init; } = "MISSING";
}

public sealed class ClusterBootstrapConfiguration
{
    public int Replicates { get; init; } = 1000;
    public decimal ConfidenceLevel { get; init; } = 0.95m;
    public string ClusterBy { get; init; } = "Fixture";
    public string SeedVersion { get; init; } = "cluster-bootstrap-v1";
}

public sealed class ThresholdGridConfiguration
{
    public bool Enabled { get; init; }
    public IReadOnlyList<decimal> MinRobustEdge { get; init; } = [0.005m];
    public IReadOnlyList<decimal> MinRobustExpectedValue { get; init; } = [0m];
    public IReadOnlyList<decimal> MinPositiveEvStability { get; init; } = [0.75m];
    public IReadOnlyList<decimal> MinScenarioSideStability { get; init; } = [0.75m];
    public IReadOnlyList<decimal> MinNormalizedWorstCaseDistance { get; init; } = [0.25m];
    public IReadOnlyList<decimal> MaxNormalizedConsensusRange { get; init; } = [0.75m];
    public IReadOnlyList<decimal> MaxNormalizedCoherenceGap { get; init; } = [0.75m];
    public IReadOnlyList<decimal> MinCalibrationReliability { get; init; } = [0.50m];
    public int MinimumApprovedTrainingPicks { get; init; } = 30;
    public int MinimumApprovedValidationPicks { get; init; } = 15;
    public ThresholdObjectiveWeights ObjectiveWeights { get; init; } = new();
    public int MaximumGridCombinations { get; init; } = 10_000;
}

public sealed class ThresholdObjectiveWeights
{
    public decimal ProfitLoss { get; init; } = 1m;
    public decimal Yield { get; init; } = 0.75m;
    public decimal Drawdown { get; init; } = 1m;
    public decimal Volume { get; init; } = 0.50m;
    public decimal Calibration { get; init; } = 0.75m;
    public decimal Clv { get; init; } = 0.50m;
}

public sealed record RobustThresholdPolicy(
    decimal MinRobustEdge,
    decimal MinRobustExpectedValue,
    decimal MinPositiveEvStability,
    decimal MinScenarioSideStability,
    decimal MinNormalizedWorstCaseDistance,
    decimal MaxNormalizedConsensusRange,
    decimal MaxNormalizedCoherenceGap,
    decimal MinCalibrationReliability)
{
    public string StableKey => FormattableString.Invariant(
        $"edge={MinRobustEdge:G29};ev={MinRobustExpectedValue:G29};evs={MinPositiveEvStability:G29};scs={MinScenarioSideStability:G29};wd={MinNormalizedWorstCaseDistance:G29};cr={MaxNormalizedConsensusRange:G29};cg={MaxNormalizedCoherenceGap:G29};cal={MinCalibrationReliability:G29}");
}

public sealed record StrategyMetrics(
    int CandidateCount,
    int ResolvedPicks,
    int ApprovedPicks,
    decimal TotalStake,
    decimal ProfitLoss,
    decimal? Yield,
    decimal MaximumDrawdown,
    int Wins,
    int HalfWins,
    int Pushes,
    int HalfLosses,
    int Losses,
    decimal? AverageOdds,
    decimal? HitRate,
    int LongestLosingStreak,
    int PointEdgeObservationCount,
    decimal? AveragePointEdge,
    int RobustEdgeObservationCount,
    decimal? AverageRobustEdge,
    int PointExpectedValueObservationCount,
    decimal? AveragePointExpectedValue,
    int RobustExpectedValueObservationCount,
    decimal? AverageRobustExpectedValue,
    int PositiveEvStabilityObservationCount,
    decimal? AveragePositiveEvStability,
    int ExposureGroupCount,
    decimal? ExposureConcentrationHhi,
    decimal? MaximumExposureShare,
    int BrierObservationCount,
    decimal? BrierScore,
    decimal? LogLoss,
    decimal? ExpectedCalibrationError,
    decimal? MeanPredictedProbability,
    decimal? MeanObservedOutcome,
    decimal? CalibrationGap,
    int CrpsObservationCount,
    decimal? AverageCrps,
    int ClvOddsObservationCount,
    decimal? AverageClvOdds,
    int ClvProbabilityObservationCount,
    decimal? AverageClvProbability,
    int ClvLineObservationCount,
    decimal? AverageClvLine);

public sealed record StrategyComparison(
    int ApprovalDisagreements,
    int BaselineOnlyApprovals,
    int RobustOnlyApprovals,
    int BothApproved,
    int BothRejected,
    int StakeReductions,
    decimal StakeReductionRate,
    decimal TotalStakeReduction,
    decimal? StakeReductionPercentage,
    int RobustRejectionsOfBaselineBets,
    decimal RobustRejectionRate,
    int AvoidedLosses,
    decimal AvoidedLossUnits,
    int AvoidedWins,
    decimal AvoidedWinProfit,
    decimal ProfitLossDelta,
    decimal? YieldDelta,
    decimal DrawdownDelta);

public sealed record MetricsComparison(
    StrategyMetrics Baseline,
    StrategyMetrics RobustShadow,
    StrategyComparison Difference);

public sealed record MetricConfidenceInterval(
    decimal Lower,
    decimal Median,
    decimal Upper);

public sealed record BootstrapConfidenceIntervals(
    string Method,
    string ClusterBy,
    int ClusterCount,
    int DayClusterCount,
    int FixtureClusterCount,
    int Replicates,
    decimal ConfidenceLevel,
    ulong DeterministicSeed,
    MetricConfidenceInterval BaselineProfitLoss,
    MetricConfidenceInterval RobustProfitLoss,
    MetricConfidenceInterval ProfitLossDelta,
    MetricConfidenceInterval? BaselineYield,
    MetricConfidenceInterval? RobustYield,
    MetricConfidenceInterval? YieldDelta,
    MetricConfidenceInterval BaselineMaximumDrawdown,
    MetricConfidenceInterval RobustMaximumDrawdown);

public sealed record ThresholdObjectiveBreakdown(
    decimal ProfitLossScore,
    decimal YieldScore,
    decimal DrawdownScore,
    decimal VolumeScore,
    decimal CalibrationScore,
    decimal ClvScore,
    decimal WeightedScore,
    IReadOnlyList<string> UnavailableComponents);

public sealed record ThresholdGridCandidateReport(
    RobustThresholdPolicy Policy,
    int ApprovedTrainingPicks,
    decimal TrainingProfitLoss,
    decimal? TrainingYield,
    decimal TrainingMaximumDrawdown,
    int ApprovedValidationPicks,
    decimal ValidationProfitLoss,
    decimal? ValidationYield,
    decimal ValidationMaximumDrawdown,
    decimal? ValidationEce,
    decimal? ValidationAverageClv,
    ThresholdObjectiveBreakdown? Objective);

public sealed record ThresholdGridFoldReport(
    int CandidatePolicyCount,
    int EligiblePolicyCount,
    IReadOnlyList<ThresholdGridCandidateReport> Candidates,
    RobustThresholdPolicy? SelectedPolicy,
    string? SelectionReason,
    ThresholdGridCandidateReport? SelectedPerformance,
    MetricsComparison? TestMetrics);

public sealed record ThresholdGridAggregateReport(
    int EligibleFoldCount,
    MetricsComparison Metrics,
    BootstrapConfidenceIntervals? BootstrapConfidenceIntervals,
    IReadOnlyList<GroupedMetricsReport> Groups);

public sealed record GroupedMetricsReport(
    string Dimension,
    string Key,
    int ObservationCount,
    MetricsComparison Metrics);

public sealed record WalkForwardFoldReport(
    int Fold,
    string EvaluationRole,
    DateTimeOffset TrainingWindowStartUtc,
    DateTimeOffset TrainingOutcomeCutoffUtc,
    DateTimeOffset ValidationWindowStartUtc,
    DateTimeOffset ValidationOutcomeCutoffUtc,
    DateTimeOffset TestStartUtc,
    DateTimeOffset TestEndUtc,
    bool IsEligible,
    string? IneligibilityReason,
    int TrainingObservationCount,
    int ExcludedUnavailableTrainingCount,
    int ValidationObservationCount,
    int ExcludedUnavailableValidationCount,
    DateTimeOffset? MaximumTrainingEvaluationAsOfUtc,
    DateTimeOffset? MaximumTrainingOutcomeAvailableUtc,
    DateTimeOffset? MaximumValidationEvaluationAsOfUtc,
    DateTimeOffset? MaximumValidationOutcomeAvailableUtc,
    bool TemporalIntegrityPassed,
    IReadOnlyList<string> TrainingEvaluationIds,
    IReadOnlyList<string> ValidationEvaluationIds,
    IReadOnlyList<string> TestEvaluationIds,
    MetricsComparison? Metrics,
    ThresholdGridFoldReport? ThresholdGrid);

public sealed record RobustPickBacktestReport(
    string ReportVersion,
    string InputSha256,
    BacktestConfiguration Configuration,
    int InputObservationCount,
    int EvaluatedObservationCount,
    int FoldCount,
    int EligibleFoldCount,
    int? FinalHoldoutFold,
    bool TemporalIntegrityPassed,
    DateTimeOffset? ReportAsOfUtc,
    IReadOnlyList<WalkForwardFoldReport> Folds,
    MetricsComparison Aggregate,
    BootstrapConfidenceIntervals? BootstrapConfidenceIntervals,
    IReadOnlyList<GroupedMetricsReport> Groups,
    ThresholdGridAggregateReport? ThresholdGridAggregate);

internal sealed record StrategyOutcome(
    ResolvedEvaluation Evaluation,
    bool Approved,
    decimal Stake,
    decimal ProfitLoss,
    decimal? Probability,
    decimal? BinaryOutcome,
    decimal? Crps,
    decimal? ClvOdds,
    decimal? ClvProbability,
    decimal? ClvLine);
