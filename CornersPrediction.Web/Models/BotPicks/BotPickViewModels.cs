using System.Text.Json;

namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotPicksIndexViewModel
{
    public BotPickFiltersViewModel Filters { get; init; } = new();

    public required BotPickMarketPageViewModel Market { get; init; }
}

public sealed record BotPickMarketPageViewModel(
    string Key,
    string Title,
    string Eyebrow,
    string Description,
    string UnitLabel,
    IReadOnlyList<BotPickMarketOptionViewModel> Options);

public sealed record BotPickMarketOptionViewModel(string Value, string Label);

public sealed class BotPickFiltersViewModel
{
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public string? Status { get; set; }

    public string? League { get; set; }

    public string? Bookmaker { get; set; }

    public string? MarketType { get; set; }

    public bool OnlyPending { get; set; }
}

public sealed class BotPickSelectionViewModel
{
    public long AutomatedCornerBetSelectionId { get; init; }
    public Guid RunId { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string AutomationVersion { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public long? MatchHistoryId { get; init; }
    public string? SourceUrl { get; init; }
    public DateTime MatchDate { get; init; }
    public DateTime? MatchDay { get; init; }
    public string League { get; init; } = string.Empty;
    public string? StandardizedLeague { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string? StandardizedHomeTeam { get; init; }
    public string? StandardizedAwayTeam { get; init; }
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string SelectedSide { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public decimal Odds { get; init; }
    public decimal Stake { get; init; }
    public decimal? FlatStake { get; init; }
    public decimal? KellyFraction { get; init; }
    public decimal? ImpliedProbability { get; init; }
    public decimal? ModelProbability { get; init; }
    public decimal? ProbabilityEdge { get; init; }
    public decimal? ExpectedValue { get; init; }
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
    public string Status { get; init; } = string.Empty;
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

    public BotPickProductionPlanViewModel? ProductionPlan { get; set; }
}

public sealed record BotPickProductionPlanViewModel(
    string Key,
    decimal StakeUnits,
    string Label,
    string Reason,
    string RowClass,
    bool IsProductive,
    string PolicyVersion = "",
    bool IsHistoricalReconstruction = false);

public sealed class BotPerformanceScorecardViewModel
{
    public int WindowDays { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public string? MarketType { get; init; }
    public string? SelectedSide { get; init; }
    public string? Bookmaker { get; init; }
    public string? AutomationVersion { get; init; }
    public int Total { get; init; }
    public int Resolved { get; init; }
    public int PredictiveResolved { get; init; }
    public int PredictiveFixtures { get; init; }
    public decimal SettledStake { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal? Yield { get; init; }
    public double? ObservedWinRate { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? AverageEdge { get; init; }
    public string TrafficLight { get; init; } = "Gray";
    public bool ProductionBlocked { get; init; }
    public string Recommendation { get; init; } = string.Empty;
}

public sealed class BotPickIntelligenceDetailViewModel
{
    public JsonElement? Latest { get; init; }

    public IReadOnlyList<BotPickIntelligenceFactViewModel> Facts { get; init; } = [];

    public IReadOnlyList<BotPickIntelligenceDocumentViewModel> Documents { get; init; } = [];

    public IReadOnlyList<BotPickIntelligenceSnapshotViewModel> Snapshots { get; init; } = [];
}

/// <summary>
/// Read model for the append-only Robust Pick Evaluation snapshot exposed by the API.
/// Every section is optional so legacy picks (and snapshots written by an older
/// evaluator version) remain readable while the contract evolves.
/// </summary>
public sealed class BotPickRobustEvaluationDetailViewModel
{
    public long PickId { get; init; }
    public BotPickRobustDecisionViewModel? Evaluation { get; init; }
    public BotPickRobustIdentityViewModel? Identity { get; init; }
    public BotPickRobustVersionsViewModel? Versions { get; init; }
    public BotPickRobustPredictionsViewModel? Predictions { get; init; }
    public BotPickRobustConsensusViewModel? Consensus { get; init; }
    public BotPickRobustDistributionViewModel? Distribution { get; init; }
    public BotPickRobustProbabilityViewModel? Probability { get; init; }
    public BotPickRobustValueViewModel? Value { get; init; }
    public BotPickRobustCalibrationViewModel? Calibration { get; init; }
    public BotPickRobustPreMatchDataViewModel? PreMatchData { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<BotPickRobustComponentViewModel> Components { get; init; } = [];
}

public sealed class BotPickRobustDecisionViewModel
{
    public long Id { get; init; }
    public int Sequence { get; init; }
    public string? Mode { get; init; }
    public string? CurrentDecision { get; init; }
    public string? RobustDecision { get; init; }
    public string? HumanReadableReason { get; init; }
    public decimal? RobustnessScore { get; init; }
    public decimal? OriginalStake { get; init; }
    public decimal? RecommendedStake { get; init; }
    public DateTime? AsOfUtc { get; init; }
    public string? EvaluationVersion { get; init; }
}

public sealed class BotPickRobustIdentityViewModel
{
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public string? MarketType { get; init; }
    public string? Side { get; init; }
    public decimal? Line { get; init; }
    public decimal? Odds { get; init; }
    public string? Bookmaker { get; init; }
    public long? FixtureId { get; init; }
    public long? SourceEvaluationId { get; init; }
    public long? SourceOddsSnapshotId { get; init; }
}

public sealed class BotPickRobustVersionsViewModel
{
    public string? MarketFamily { get; init; }
    public string? AutomationVersion { get; init; }
    public string? BaseModelVersion { get; init; }
    public string? PredictionModelVersion { get; init; }
    public string? SelectorVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? IntelligenceVersion { get; init; }
    public string? SettlementVersion { get; init; }
    public string? RobustnessVersion { get; init; }
    public string? PolicyVersion { get; init; }
}

public sealed class BotPickRobustPredictionsViewModel
{
    public decimal? Direct { get; init; }
    public decimal? Home { get; init; }
    public decimal? Away { get; init; }
    public decimal? Components { get; init; }
    public decimal? Context { get; init; }
    public decimal? Reconciled { get; init; }
}

public sealed class BotPickRobustConsensusViewModel
{
    public string? RecommendedSide { get; init; }
    public bool? SideAgreement { get; init; }
    public decimal? Range { get; init; }
    public decimal? CoherenceGap { get; init; }
    public decimal? WorstCasePrediction { get; init; }
    public decimal? WorstCaseDistance { get; init; }
    public decimal? NormalizedWorstCaseDistance { get; init; }
    public decimal? DirectDistance { get; init; }
    public decimal? ComponentsDistance { get; init; }
    public decimal? ContextDistance { get; init; }
    public decimal? ReconciledDistance { get; init; }
    public decimal? MagnitudeAgreement { get; init; }
    public decimal? ProbabilityAgreement { get; init; }
    public decimal? CoherenceScore { get; init; }
    public decimal? ScenarioStability { get; init; }
}

public sealed class BotPickRobustDistributionViewModel
{
    public decimal? P10 { get; init; }
    public decimal? P50 { get; init; }
    public decimal? P90 { get; init; }
    public decimal? PWin { get; init; }
    public decimal? PHalfWin { get; init; }
    public decimal? PPush { get; init; }
    public decimal? PHalfLoss { get; init; }
    public decimal? PLoss { get; init; }
    public decimal? ErrorScale { get; init; }
    public decimal? EffectiveN { get; init; }
    public int? RawObservationCount { get; init; }
    public int? SimulationCount { get; init; }
    public string? Method { get; init; }
    public string? DistributionMethod { get; init; }
    public string? Version { get; init; }
    public string? DistributionVersion { get; init; }
}

public sealed class BotPickRobustProbabilityViewModel
{
    public decimal? Central { get; init; }
    public decimal? Lower { get; init; }
    public decimal? Upper { get; init; }
    public decimal? Fair { get; init; }
    public decimal? MarketImplied { get; init; }
    public decimal? MarketNoVig { get; init; }
    public decimal? ConservativeMarket { get; init; }
    public decimal? Raw { get; init; }
    public decimal? BeforeCalibration { get; init; }
    public decimal? AfterCalibration { get; init; }
}

public sealed class BotPickRobustValueViewModel
{
    public decimal? PointEdge { get; init; }
    public decimal? RobustEdge { get; init; }
    public decimal? PointEv { get; init; }
    public decimal? RobustEv { get; init; }
    public decimal? PositiveEvStability { get; init; }
}

public sealed class BotPickRobustCalibrationViewModel
{
    public decimal? EffectiveN { get; init; }
    public decimal? Reliability { get; init; }
    public string? FallbackLevel { get; init; }
    public int? ExactMarketN { get; init; }
    public int? FamilyN { get; init; }
    public int? GlobalN { get; init; }
    public decimal? ProbabilityBefore { get; init; }
    public decimal? ProbabilityAfter { get; init; }
    public decimal? ProbabilityLower { get; init; }
    public decimal? ProbabilityUpper { get; init; }
    public decimal? PriorWeight { get; init; }
    public string? IntervalMethod { get; init; }
    public decimal? ConfidenceLevel { get; init; }
}

public sealed class BotPickRobustPreMatchDataViewModel
{
    public string? LineupStatus { get; init; }
    public string? IntelligenceEvidenceStatus { get; init; }
    public string? FatigueDataStatus { get; init; }
    public string? GameStateModelStatus { get; init; }
    public int? SnapshotAgeSeconds { get; init; }
    public int? SnapshotAgeMinutes { get; init; }
    public int? OddsAgeSeconds { get; init; }
    public string? OddsAvailabilityStatus { get; init; }
    public int? ActionableFacts { get; init; }
    public int? ActionableFactCount { get; init; }
    public int? IndependentSources { get; init; }
    public int? IndependentSourceCount { get; init; }
    public DateTime? QuoteTimestampUtc { get; init; }
    public decimal? OddsReliability { get; init; }
}

public sealed class BotPickRobustComponentViewModel
{
    public int Sequence { get; init; }
    public int ComponentSequence { get; init; }
    public string? Type { get; init; }
    public string? ComponentType { get; init; }
    public decimal? PredictedValue { get; init; }
    public decimal? ProbabilityForSelection { get; init; }
    public decimal? Weight { get; init; }
    public bool? IsUsable { get; init; }
    public string? SourceVersion { get; init; }
    public string? ExclusionReason { get; init; }
    public decimal? DataQualityScore { get; init; }
}

public sealed class BotPickIntelligenceSnapshotViewModel
{
    public long Id { get; init; }
    public long FixtureId { get; init; }
    public int TeamId { get; init; }
    public bool IsHomeTeam { get; init; }
    public DateTime CutoffAtUtc { get; init; }
    public int IndependentSourceCount { get; init; }
    public int ActionableFactCount { get; init; }
    public int ConfirmedOutCount { get; init; }
    public int DoubtfulCount { get; init; }
    public int SuspendedCount { get; init; }
    public decimal AttackAvailabilityImpact { get; init; }
    public decimal DefenceAvailabilityImpact { get; init; }
    public decimal GoalkeeperAvailabilityImpact { get; init; }
    public decimal WidthAvailabilityImpact { get; init; }
    public decimal SetPieceAvailabilityImpact { get; init; }
    public decimal CornerCreationImpact { get; init; }
    public decimal MissingShotShare { get; init; }
    public decimal ShotGenerationImpact { get; init; }
    public decimal MissingSotShare { get; init; }
    public decimal FinishingAvailabilityImpact { get; init; }
    public decimal MissingGoalShare { get; init; }
    public decimal GoalScoringAvailabilityImpact { get; init; }
    public decimal OverallNewsConfidence { get; init; }
    public int ConflictCount { get; init; }
    public int SnapshotAgeMinutes { get; init; }
}

public sealed class BotPickIntelligenceFactViewModel
{
    public long Id { get; init; }
    public long NewsDocumentId { get; init; }
    public long FixtureId { get; init; }
    public int? TeamId { get; init; }
    public int? PlayerId { get; init; }
    public string TeamNameExtracted { get; init; } = string.Empty;
    public string? PlayerNameExtracted { get; init; }
    public string? PositionCode { get; init; }
    public int EventType { get; init; }
    public int AvailabilityStatus { get; init; }
    public int Certainty { get; init; }
    public string? Reason { get; init; }
    public string EvidenceSnippet { get; init; } = string.Empty;
    public decimal EffectiveConfidence { get; init; }
    public bool IsCurrent { get; init; }
    public DateTime FirstSeenAtUtc { get; init; }
}

public sealed class BotPickIntelligenceDocumentViewModel
{
    public long Id { get; init; }
    public long FixtureId { get; init; }
    public int? TeamId { get; init; }
    public string Url { get; init; } = string.Empty;
    public string SourceDomain { get; init; } = string.Empty;
    public int SourceTier { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime? PublishedAtUtc { get; init; }
    public DateTime RetrievedAtUtc { get; init; }
    public int ExtractionStatus { get; init; }
}

public sealed class UpdateBotPickStatusViewModel
{
    public string Status { get; set; } = string.Empty;

    public int? ActualHomeCorners { get; set; }

    public int? ActualAwayCorners { get; set; }

    public int? ActualTotalCorners { get; set; }
}

public sealed class ResolveBotPickViewModel
{
    public int ActualValue { get; set; }
}

public sealed class SettlePendingBotPicksViewModel
{
    public DateTime? MatchDateTo { get; set; }

    public int MaxRows { get; set; } = 20000;

    public string? BotKey { get; set; }

    public string? MarketFamily { get; set; }
}

public sealed class ReconcileAvailableBotPicksViewModel
{
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    public int MaxSelections { get; set; } = 20000;

    public bool DryRun { get; set; }
}

public sealed class ReconcileAvailableBotPicksResponseViewModel
{
    public DateTime? DateFrom { get; init; }
    public DateTime DateTo { get; init; }
    public bool DryRun { get; init; }
    public int InitialReviewed { get; init; }
    public int InitialSettled { get; init; }
    public int PendingAfterLocalSettlement { get; init; }
    public int FixtureDatesQueried { get; init; }
    public int FixturesDiscovered { get; init; }
    public int MatchedSelections { get; init; }
    public int UniqueMatchedFixtures { get; init; }
    public int SyncedFixtures { get; init; }
    public int LinkedSelections { get; init; }
    public int UnmatchedSelections { get; init; }
    public int AmbiguousSelections { get; init; }
    public int MissingMarketStatistics { get; init; }
    public int FinalReviewed { get; init; }
    public int FinalSettled { get; init; }
    public int FinalWon { get; init; }
    public int FinalLost { get; init; }
    public int FinalPush { get; init; }
    public int StillPending { get; init; }
    public string? DailyRemaining { get; init; }
    public string? MinuteRemaining { get; init; }
}

public sealed class BotPickSettlementResponseViewModel
{
    public DateTime? MatchDateTo { get; init; }
    public bool DryRun { get; init; }
    public int ReviewedRows { get; init; }
    public int SettledRows { get; init; }
    public int StillPendingRows { get; init; }
    public int WonRows { get; init; }
    public int LostRows { get; init; }
    public int PushRows { get; init; }
    public int AppliedRows { get; init; }
    public int ConcurrentlySkippedRows { get; init; }
    public string? BotKey { get; init; }
    public string? MarketFamily { get; init; }
    public IReadOnlyList<BotPickSettlementItemViewModel> Items { get; init; } = [];
}

public sealed class BotPickSettlementItemViewModel
{
    public long SelectionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class BotPickMonthlySummaryViewModel
{
    public DateTime Month { get; init; }
    public string BotKey { get; init; } = string.Empty;
    public string BotLabel { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Push { get; init; }
    public int Void { get; init; }
    public decimal ProfitLoss { get; init; }
    public decimal SettledStake { get; init; }
    public decimal? YieldPct { get; init; }
}
