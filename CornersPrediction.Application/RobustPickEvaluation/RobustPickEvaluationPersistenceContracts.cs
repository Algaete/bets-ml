namespace CornersPrediction.Application.RobustPickEvaluation;

/// <summary>
/// Persistence boundary for immutable robust-evaluation evidence.  It deliberately
/// uses persistence DTOs rather than domain services so the numerical core remains
/// independent of SQL Server.
/// </summary>
public interface IRobustPickEvaluationRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task<AppendRobustEvaluationResult> AppendAsync(
        AppendRobustPickEvaluationCommand command,
        CancellationToken cancellationToken);

    Task<RobustPickEvaluationDetail?> GetCurrentBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RobustPickEvaluationSnapshot>> GetHistoryBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken);

    Task<RobustEvaluationComparisonDto?> GetComparisonBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken);

    Task<RobustEvaluationMetricsDto> GetMetricsAsync(
        RobustEvaluationMetricsFilter filter,
        CancellationToken cancellationToken);

    Task<RobustBackfillPreviewResult> PreviewBackfillAsync(
        RobustBackfillPreviewFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RobustBackfillCandidateDto>> LoadBackfillCandidatesAsync(
        RobustBackfillPreviewFilter filter,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RobustResidualObservation>> LoadResidualHistoryAsync(
        RobustResidualHistoryQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OpenPortfolioExposureDto>> LoadOpenExposureAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken);

    Task<AppendRobustPolicyResult> AppendPolicyAsync(
        AppendRobustPolicyCommand command,
        CancellationToken cancellationToken);

    Task<RobustPolicySnapshot?> GetEffectivePolicyAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RobustPolicySnapshot>> GetPolicyHistoryAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken);
}

public class AppendRobustPickEvaluationCommand
{
    public long? SourceEvaluationId { get; init; }
    public long? BotPickSelectionId { get; set; }
    public long? SourceOddsSnapshotId { get; init; }
    public long? FixtureId { get; init; }
    public string EvaluationSubjectKey { get; init; } = string.Empty;
    public string BotKey { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal Line { get; init; }
    public decimal Odds { get; init; }
    public string Bookmaker { get; init; } = string.Empty;
    public string EvaluationVersion { get; init; } = string.Empty;
    public DateTime AsOfUtc { get; init; }

    public string? BaseModelVersion { get; init; }
    public DateTime? ModelTrainedThroughUtc { get; init; }
    public string? SelectorVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? IntelligenceVersion { get; init; }
    public string? SettlementVersion { get; init; }
    public string RobustnessVersion { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = string.Empty;

    public decimal? DirectPrediction { get; init; }
    public decimal? HomePrediction { get; init; }
    public decimal? AwayPrediction { get; init; }
    public decimal? ComponentsPrediction { get; init; }
    public decimal? ContextPrediction { get; init; }
    public decimal? ReconciledPrediction { get; init; }
    public decimal? ConsensusMinimum { get; init; }
    public decimal? ConsensusMaximum { get; init; }
    public decimal? ConsensusRange { get; init; }
    public decimal? CoherenceGap { get; init; }

    public decimal? DirectDistance { get; init; }
    public decimal? ComponentsDistance { get; init; }
    public decimal? ContextDistance { get; init; }
    public decimal? ReconciledDistance { get; init; }
    public decimal? WorstCasePrediction { get; init; }
    public decimal? WorstCaseDistance { get; init; }
    public decimal? ErrorScale { get; init; }
    public decimal? NormalizedDirectDistance { get; init; }
    public decimal? NormalizedWorstCaseDistance { get; init; }
    public decimal? NormalizedConsensusRange { get; init; }
    public decimal? NormalizedCoherenceGap { get; init; }

    public bool? SideAgreement { get; init; }
    public decimal? MagnitudeAgreementScore { get; init; }
    public decimal? ProbabilityAgreementScore { get; init; }
    public decimal? CoherenceScore { get; init; }
    public decimal? ScenarioSideStability { get; init; }
    public decimal? PositiveEvStability { get; init; }

    public decimal? P01 { get; init; }
    public decimal? P05 { get; init; }
    public decimal? P10 { get; init; }
    public decimal? P25 { get; init; }
    public decimal? P50 { get; init; }
    public decimal? P75 { get; init; }
    public decimal? P90 { get; init; }
    public decimal? P95 { get; init; }
    public decimal? P99 { get; init; }
    public decimal? DistributionMean { get; init; }
    public decimal? StandardDeviation { get; init; }
    public decimal? MedianAbsoluteDeviation { get; init; }
    public decimal? DistributionEffectiveN { get; init; }
    public int? ResidualRawObservationCount { get; init; }
    public int? SimulationCount { get; init; }
    public string? DistributionMethod { get; init; }
    public string? DistributionVersion { get; init; }
    public string HistogramJson { get; init; } = "[]";
    public decimal? PWin { get; init; }
    public decimal? PHalfWin { get; init; }
    public decimal? PPush { get; init; }
    public decimal? PHalfLoss { get; init; }
    public decimal? PLoss { get; init; }

    public decimal? RawProbability { get; init; }
    public decimal? CalibratedProbability { get; init; }
    public decimal? ProbabilityLowerBound { get; init; }
    public decimal? ProbabilityUpperBound { get; init; }
    public decimal? ModelFairOdds { get; init; }
    public decimal? ModelFairProbability { get; init; }
    public decimal? RobustModelFairProbability { get; init; }
    public decimal? MarketImpliedProbability { get; init; }
    public decimal? MarketNoVigProbability { get; init; }
    public decimal? ConservativeMarketProbability { get; init; }

    public decimal? PointEdge { get; init; }
    public decimal? RobustEdge { get; init; }
    public decimal? PointExpectedValue { get; init; }
    public decimal? RobustExpectedValue { get; init; }
    public decimal? ExpectedValueP10 { get; init; }
    public decimal? ExpectedValueP50 { get; init; }
    public decimal? ExpectedValueP90 { get; init; }
    public decimal? EdgeP10 { get; init; }
    public decimal? EdgeP50 { get; init; }
    public decimal? EdgeP90 { get; init; }

    public decimal? CalibrationEffectiveN { get; init; }
    public int? CalibrationExactMarketN { get; init; }
    public int? CalibrationFamilyN { get; init; }
    public int? CalibrationGlobalN { get; init; }
    public decimal? CalibrationReliability { get; init; }
    public decimal? CalibrationSpecificityScore { get; init; }
    public decimal? CalibrationRecencyScore { get; init; }
    public decimal? CalibrationErrorScore { get; init; }
    public string? CalibrationFallbackLevel { get; init; }

    public decimal? OddsEvaluated { get; init; }
    public decimal? OddsTaken { get; init; }
    public decimal? OpeningOdds { get; init; }
    public decimal? ClosingOdds { get; init; }
    public decimal? BestAvailableOdds { get; init; }
    public decimal? MedianMarketOdds { get; init; }
    public DateTime? QuoteTimestampUtc { get; init; }
    public int? OddsAgeSeconds { get; init; }
    public int? MinutesToKickoff { get; init; }
    public string? NoVigMethod { get; init; }
    public decimal? OddsReliability { get; init; }
    public decimal? OpeningLine { get; init; }
    public decimal? ClosingLine { get; init; }
    public decimal? ClvOdds { get; init; }
    public decimal? ClvLine { get; init; }

    public string? LineupStatus { get; init; }
    public string? IntelligenceEvidenceStatus { get; init; }
    public string? FatigueDataStatus { get; init; }
    public string? GameStateModelStatus { get; init; }
    public int? ScenarioCount { get; init; }
    public decimal? AdverseScenarioProbability { get; init; }
    public decimal? ScenarioStability { get; init; }

    public string EvaluationMode { get; init; } = "Shadow";
    public string CurrentSystemDecision { get; init; } = string.Empty;
    public string RobustDecision { get; init; } = string.Empty;
    public decimal OriginalStake { get; init; }
    public decimal RecommendedStake { get; init; }
    public decimal StakeMultiplier { get; init; }
    public decimal? RobustnessScore { get; init; }
    public string RejectionReasonCodesJson { get; init; } = "[]";
    public string WarningCodesJson { get; init; } = "[]";
    public string HumanReadableReason { get; init; } = string.Empty;
    public string InputPayloadJson { get; init; } = "{}";
    public string EvaluationPayloadJson { get; init; } = "{}";
    public IReadOnlyList<RobustEvaluationComponentSnapshot> Components { get; init; } = [];
}

public sealed class RobustEvaluationComponentSnapshot
{
    public long RobustComponentId { get; init; }
    public long RobustEvaluationId { get; init; }
    public int ComponentSequence { get; init; }
    public string ComponentType { get; init; } = string.Empty;
    public decimal? PredictedValue { get; init; }
    public decimal? ProbabilityForSelection { get; init; }
    public decimal Weight { get; init; }
    public bool IsUsable { get; init; }
    public string? SourceVersion { get; init; }
    public DateTime AsOfUtc { get; init; }
    public string? ExclusionReason { get; init; }
    public decimal? DataQualityScore { get; init; }
    public string MetadataJson { get; init; } = "{}";
}

public sealed class RobustPickEvaluationSnapshot : AppendRobustPickEvaluationCommand
{
    public long RobustEvaluationId { get; init; }
    public string LogicalPickKey { get; init; } = string.Empty;
    public string IdempotencyHash { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string SnapshotHash { get; init; } = string.Empty;
    public int EvaluationSequence { get; init; }
    public bool IsCurrent { get; init; }
    public long? SupersedesEvaluationId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record RobustPickEvaluationDetail(
    RobustPickEvaluationSnapshot Evaluation,
    IReadOnlyList<RobustEvaluationComponentSnapshot> Components);

public sealed record AppendRobustEvaluationResult(
    long RobustEvaluationId,
    int EvaluationSequence,
    bool Inserted,
    long? SupersedesEvaluationId);

public sealed class RobustEvaluationComparisonDto
{
    public long BotPickSelectionId { get; init; }
    public long RobustEvaluationId { get; init; }
    public int EvaluationSequence { get; init; }
    public string EvaluationMode { get; init; } = string.Empty;
    public string CurrentDecision { get; init; } = string.Empty;
    public string ShadowDecision { get; init; } = string.Empty;
    public decimal OriginalStake { get; init; }
    public decimal RecommendedStake { get; init; }
    public decimal StakeDifference { get; init; }
    public decimal? RobustnessScore { get; init; }
    public string RejectionReasonCodesJson { get; init; } = "[]";
    public string WarningCodesJson { get; init; } = "[]";
    public string HumanReadableReason { get; init; } = string.Empty;
    public DateTime AsOfUtc { get; init; }
}

public sealed record RobustEvaluationMetricsFilter(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? BotKey = null,
    string? MarketFamily = null,
    string? MarketType = null,
    string? EvaluationVersion = null);

public sealed class RobustEvaluationMetricsDto
{
    public long Evaluated { get; init; }
    public long ShadowApproved { get; init; }
    public long ShadowRejected { get; init; }
    public long ShadowReducedStake { get; init; }
    public long ShadowManualReview { get; init; }
    public long DecisionDisagreements { get; init; }
    public long Resolved { get; init; }
    public decimal? BaselineStake { get; init; }
    public decimal? BaselineProfitLoss { get; init; }
    public decimal? BaselineYield { get; init; }
    public decimal? RobustShadowStake { get; init; }
    public decimal? RobustShadowProfitLoss { get; init; }
    public decimal? RobustShadowYield { get; init; }
    public decimal? RobustMaximumDrawdown { get; init; }
    public decimal? AverageClvOdds { get; init; }
    public decimal? AverageRobustnessScore { get; init; }
    public decimal? AveragePointEdge { get; init; }
    public decimal? AverageRobustEdge { get; init; }
    public decimal? AveragePointExpectedValue { get; init; }
    public decimal? AverageRobustExpectedValue { get; init; }
    public IReadOnlyList<RobustReasonMetricDto> Reasons { get; init; } = [];
}

public sealed class RobustReasonMetricDto
{
    public string ReasonCode { get; init; } = string.Empty;
    public long Occurrences { get; init; }
}

public sealed record RobustBackfillPreviewFilter(
    DateTime FromUtc,
    DateTime ToUtc,
    string? BotKey,
    string? MarketFamily,
    string? MarketType,
    long? FixtureId,
    string EvaluationVersion,
    bool DryRun = true,
    bool Force = false,
    DateTime? AfterPredictionTimestampUtc = null,
    long? AfterSourceEvaluationId = null);

public sealed class RobustBackfillPreviewResult
{
    public bool DryRun { get; init; } = true;
    public long SourceCandidates { get; init; }
    public long EligibleCandidates { get; init; }
    public long AlreadyEvaluated { get; init; }
    public long MissingPredictionTimestamp { get; init; }
    public long MissingModelTrainingCutoff { get; init; }
    public long ModelTrainedAfterPrediction { get; init; }
    public long MissingImmutableOddsSnapshot { get; init; }
    public long OddsSnapshotAfterPrediction { get; init; }
    public long MissingBilateralOdds { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Immutable, leakage-safe source material for one historical robust evaluation.
/// The odds are always read from the exact referenced CornerOddsSnapshots row;
/// mutable current-odds columns are deliberately not used as a fallback.
/// </summary>
public sealed class RobustBackfillCandidateDto
{
    public long SourceEvaluationId { get; init; }
    public long? PublishedSelectionId { get; init; }
    public long SourceOddsSnapshotId { get; init; }
    public long FixtureId { get; init; }
    public long? ExternalFixtureId { get; init; }
    public long PartidoProximoCuotaId { get; init; }
    public DateTime MatchDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public DateTime OddsTimestampUtc { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string AutomationVersion { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Bookmaker { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public string MarketFamily { get; init; } = string.Empty;
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public decimal Line { get; init; }
    public decimal SelectedOdds { get; init; }
    public decimal OverOdds { get; init; }
    public decimal UnderOdds { get; init; }
    public decimal PrimaryPrediction { get; init; }
    public decimal? DirectPrediction { get; init; }
    public decimal? ContextPrediction { get; init; }
    public decimal? HomePrediction { get; init; }
    public decimal? AwayPrediction { get; init; }
    public decimal RawProbability { get; init; }
    public decimal CalibratedProbability { get; init; }
    public decimal? ProbabilityLowerBound { get; init; }
    public decimal? ProbabilityUpperBound { get; init; }
    public decimal DataQualityScore { get; init; }
    public decimal OriginalStake { get; init; }
    public string BaseModelVersion { get; init; } = string.Empty;
    public DateTime ModelTrainedThroughUtc { get; init; }
    public string? SelectorVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? IntelligenceVersion { get; init; }
    public string FeatureSnapshotJson { get; init; } = "{}";
}

public sealed record RobustResidualHistoryQuery(
    DateTime EvaluationAsOfUtc,
    string MarketFamily,
    string? MarketType = null,
    string? Side = null,
    string? League = null,
    int OutcomeAvailabilityLagHours = 8,
    int MaximumRows = 20_000);

public sealed class RobustResidualObservation
{
    public long SourceEvaluationId { get; init; }
    public long MatchHistoryId { get; init; }
    public long? FixtureId { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public decimal Line { get; init; }
    public decimal? Odds { get; init; }
    public decimal Prediction { get; init; }
    public decimal ActualResult { get; init; }
    public decimal Residual { get; init; }
    public DateTime FixtureStartUtc { get; init; }
    /// <summary>
    /// Conservative end bound.  When the source lacks an official final-whistle
    /// timestamp this is the later outcome-availability timestamp, never an
    /// invented 90/120-minute estimate.
    /// </summary>
    public DateTime FixtureEndUtc { get; init; }
    public DateTime PredictionAsOfUtc { get; init; }
    public DateTime? ModelTrainedThroughUtc { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public string? ModelVersion { get; init; }
    public string ResidualSource { get; init; } = "SelectedPicksOnly";
    public decimal DataQualityScore { get; init; }
}

public sealed class OpenPortfolioExposureDto
{
    public long BotPickSelectionId { get; init; }
    public long? FixtureId { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public DateTime MatchDate { get; init; }
    public decimal Stake { get; init; }
    public decimal? RobustnessScore { get; init; }
    public string CorrelationCluster { get; init; } = string.Empty;
}

public sealed record AppendRobustPolicyCommand(
    string PolicyVersion,
    DateTime EffectiveFromUtc,
    string EvaluationMode,
    string? BotKey = null,
    string? MarketFamily = null,
    string? MarketType = null,
    string? MarketScope = null,
    string? Side = null,
    decimal? MinimumLine = null,
    decimal? MaximumLine = null,
    decimal? MinimumOdds = null,
    decimal? MaximumOdds = null,
    string? LeaguePattern = null,
    string ConfigurationJson = "{}",
    string CreatedBy = "system");

public sealed record RobustPolicyQuery(
    DateTime AsOfUtc,
    string? BotKey = null,
    string? MarketFamily = null,
    string? MarketType = null,
    string? MarketScope = null,
    string? Side = null,
    string? League = null,
    decimal? Line = null,
    decimal? Odds = null);

public sealed class RobustPolicySnapshot
{
    public long RobustPolicyId { get; init; }
    public string PolicyHash { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = string.Empty;
    public DateTime EffectiveFromUtc { get; init; }
    public string EvaluationMode { get; init; } = "Shadow";
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public string? MarketType { get; init; }
    public string? MarketScope { get; init; }
    public string? Side { get; init; }
    public decimal? MinimumLine { get; init; }
    public decimal? MaximumLine { get; init; }
    public decimal? MinimumOdds { get; init; }
    public decimal? MaximumOdds { get; init; }
    public string? LeaguePattern { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record AppendRobustPolicyResult(long RobustPolicyId, bool Inserted);
