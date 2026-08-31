namespace CornersPrediction.Application.Automation.BotG;

public sealed record BotGCandidateAuditFilter(
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null,
    string? Decision = null,
    string? PublicationStatus = null,
    string? MarketType = null,
    string? Selection = null,
    string? Bookmaker = null,
    string? ConfigurationVersion = null,
    string? Result = null,
    int Page = 1,
    int PageSize = 50);

public sealed record BotGScorecardFilter(
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null,
    string? ConfigurationVersion = null);

public sealed record SettleBotG2026CandidatesCommand(
    DateTime? OutcomeAvailableThroughUtc = null,
    int MaximumCandidates = 5000,
    bool DryRun = false);

public sealed record SettleBotG2026CandidatesResult(
    int ScannedCandidates,
    int EligibleCandidates,
    int SettledCandidates,
    int UnmatchedOrUnavailableCandidates,
    long RemainingPendingCandidates,
    bool DryRun,
    DateTime SettledAtUtc);

public sealed record BotGCandidateAuditPage(
    IReadOnlyList<BotGCandidateAuditDto> Items,
    long TotalRows,
    int Page,
    int PageSize);

public class BotGCandidateAuditDto
{
    public long CandidateId { get; init; }
    public Guid CandidateUuid { get; init; }
    public Guid RunId { get; init; }
    public string BotKey { get; init; } = "G2026";
    public string AutomationVersion { get; init; } = string.Empty;
    public long? SourceOddsId { get; init; }
    public long? OddsSnapshotId { get; init; }
    public DateTime OddsTimestampUtc { get; init; }
    public long FixtureId { get; init; }
    public long? OfficialFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Bookmaker { get; init; } = string.Empty;
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketFamily { get; init; } = "GOALS";
    public string MarketType { get; init; } = string.Empty;
    public decimal Line { get; init; }
    public string Selection { get; init; } = string.Empty;
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal SelectedOdds { get; init; }
    public double? RawImpliedProbability { get; init; }
    public double? MarketNoVigProbability { get; init; }
    public double? LegacyPrediction { get; init; }
    public double? Prediction2026 { get; init; }
    public double? ContextPrediction { get; init; }
    public double? HistoricalMean { get; init; }
    public double? HistoricalMedian { get; init; }
    public double? HistoricalStd { get; init; }
    public double? PredictionMinusLine { get; init; }
    public double? LegacyMinusMarketEquivalent { get; init; }
    public double? Model2026MinusMarketEquivalent { get; init; }
    public double? CandidateProbability { get; init; }
    public double? CalibratedProbability { get; init; }
    public double? FinalProbability { get; init; }
    public double? ProbabilityLowerBound { get; init; }
    public double? ProbabilityUpperBound { get; init; }
    public double? ConservativeProbability { get; init; }
    public double? RawEdge { get; init; }
    public double? ConservativeEdge { get; init; }
    public double? RawExpectedValue { get; init; }
    public double? ConservativeExpectedValue { get; init; }
    public double? DataQualityScore { get; init; }
    public double? ContextAgreementScore { get; init; }
    public double? UncertaintyScore { get; init; }
    public double? CalibrationReliability { get; init; }
    public double? OutOfDistributionScore { get; init; }
    public double? ModelDisagreement { get; init; }
    public double? GSelectionScore { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionReason { get; init; } = string.Empty;
    public string DecisionReasonsJson { get; init; } = "[]";
    public string RiskFlagsJson { get; init; } = "[]";
    public bool Approved { get; init; }
    public bool Published { get; init; }
    public string PublicationStatus { get; init; } = "Shadow";
    public long? PublishedSelectionId { get; init; }
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string? BaseModelVersion { get; init; }
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public string? MetaModelVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? UncertaintyVersion { get; init; }
    public string? OodVersion { get; init; }
    public decimal? StakeUnits { get; init; }
    public string? Result { get; init; }
    public decimal? ActualValue { get; init; }
    public decimal? SettlementFactor { get; init; }
    public decimal? ProfitLoss { get; init; }
    public string? SettlementState { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public DateTime? SettledAtUtc { get; init; }
    public decimal? ClosingLine { get; init; }
    public decimal? ClosingOdds { get; init; }
    public double? ClosingMarketNoVigProbability { get; init; }
    public DateTime? ClosingCapturedAtUtc { get; init; }
    public string FeatureSnapshotJson { get; init; } = "{}";
    public DateTime EvaluatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public long TotalRows { get; init; }
}

public sealed class BotGScorecardDto
{
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public long CandidatesEvaluated { get; init; }
    public long FixturesEvaluated { get; init; }
    public long ResolvedFixtures { get; init; }
    public long CandidatesApproved { get; init; }
    public long CandidatesRejected { get; init; }
    public long CandidatesAbstained { get; init; }
    public long CandidatesPublished { get; init; }
    public long Resolved { get; init; }
    public long PredictiveResolved { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public long Voids { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? HitRate { get; init; }
    public double? AverageOdds { get; init; }
    public double? AverageRawEdge { get; init; }
    public double? AverageConservativeEdge { get; init; }
    public double? AverageRawExpectedValue { get; init; }
    public double? AverageConservativeExpectedValue { get; init; }
    public double? ActualYield { get; init; }
    public double? ExpectedValueYieldGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? LogLoss { get; init; }
    public double? MarketLogLoss { get; init; }
    public double? DeltaLogLoss { get; init; }
    public double? Ece { get; init; }
    public double? CalibrationSlope { get; init; }
    public double? CalibrationIntercept { get; init; }
    public double? MaximumDrawdown { get; init; }
    public double? ProfitFactor { get; init; }
    public double? CoverageRate { get; init; }
    public double? PublicationRate { get; init; }
    public double? AverageOddsClv { get; init; }
    public double? AverageLineClv { get; init; }
    public double? AverageUncertainty { get; init; }
    public double? AverageCalibrationReliability { get; init; }
    public double? AverageOutOfDistributionScore { get; init; }
    public string SuggestedPromotionStage { get; init; } = "SHADOW";
}

public interface IBotGCandidateReadRepository
{
    Task<BotGCandidateAuditPage> GetCandidatesAsync(
        BotGCandidateAuditFilter filter,
        CancellationToken cancellationToken);

    Task<BotGCandidateAuditDto?> GetCandidateAsync(
        long candidateId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BotGScorecardDto>> GetScorecardAsync(
        BotGScorecardFilter filter,
        CancellationToken cancellationToken);

    Task<SettleBotG2026CandidatesResult> SettlePendingAsync(
        SettleBotG2026CandidatesCommand command,
        CancellationToken cancellationToken);
}
