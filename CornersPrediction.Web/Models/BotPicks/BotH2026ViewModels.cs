namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotH2026FiltersViewModel
{
    public DateTime? PredictionFromUtc { get; set; }
    public DateTime? PredictionToUtc { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Decision { get; set; }
    public string? MarketType { get; set; }
    public string? Selection { get; set; }
    public string? ConfigurationVersion { get; set; }
    public string? SettlementState { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

/// <summary>
/// Read-only parameters for replaying the immutable H2026 evidence with alternate
/// thresholds. These values never mutate a captured decision or publish a pick.
/// </summary>
public sealed class BotH2026ThresholdAnalysisFiltersViewModel
{
    public DateTime? AsOfUtc { get; set; }
    public string? ConfigurationVersion { get; set; }
    public string? MarketType { get; set; }
    public string? Selection { get; set; }
    public string AnalysisVersion { get; set; } = "bot-h-threshold-what-if-1.0.0";
    public decimal MinimumFinalProbability { get; set; } = 0.56m;
    public decimal MinimumFinalEdge { get; set; } = 0.04m;
    public decimal MinimumFinalExpectedValue { get; set; } = 0.03m;
    public decimal MinimumDataQualityScore { get; set; } = 0.70m;
    public decimal MinimumContextAgreementScore { get; set; } = 0.70m;
    public decimal MinimumOdds { get; set; } = 1.60m;
    public decimal MaximumOdds { get; set; } = 2.20m;
    public decimal DevelopmentFraction { get; set; } = 0.70m;
}

public sealed class BotH2026IndexViewModel
{
    public BotH2026FiltersViewModel Filters { get; init; } = new();
    public BotH2026ThresholdAnalysisFiltersViewModel ThresholdFilters { get; init; } = new();
    public BotH2026EvaluationPageViewModel Evaluations { get; init; } = new();
    public IReadOnlyList<BotH2026ScorecardViewModel> Scorecards { get; init; } = [];
    public IReadOnlyList<BotH2026ThresholdAnalysisViewModel> ThresholdAnalysis { get; init; } = [];
    public BotH2026StatusViewModel Status { get; init; } = new();
    public string? ErrorMessage { get; init; }
    public string? StatusErrorMessage { get; init; }
    public string? EvaluationsErrorMessage { get; init; }
    public string? ScorecardsErrorMessage { get; init; }
    public string? ThresholdAnalysisErrorMessage { get; init; }
    public bool ThresholdAnalysisRequested { get; init; }

    public bool StatusAvailable => string.IsNullOrWhiteSpace(StatusErrorMessage);
    public bool EvaluationsAvailable => string.IsNullOrWhiteSpace(EvaluationsErrorMessage);
    public bool ScorecardsAvailable => string.IsNullOrWhiteSpace(ScorecardsErrorMessage);
    public bool ThresholdAnalysisAvailable => string.IsNullOrWhiteSpace(ThresholdAnalysisErrorMessage);
}

public sealed class BotH2026StatusViewModel
{
    public string BotKey { get; init; } = "H2026";
    public bool SchemaReady { get; init; }
    public bool DefinitionExists { get; init; }
    public bool IsEnabled { get; init; }
    public bool PublishEnabled { get; init; }
    public bool ShadowOnly { get; init; }
    public bool CaptureTriggerEnabled { get; init; }
    public bool PublicationGuardsEnabled { get; init; }
    public long CapturedEvaluations { get; init; }
    public long UnsafePublicationRows { get; init; }
    public long UncapturedEligibleEvaluations { get; init; }
    public DateTime? FirstPredictionTimestampUtc { get; init; }
    public DateTime? LastPredictionTimestampUtc { get; init; }
    public string State { get; init; } = "NOT_READY";
}

public sealed class BotH2026EvaluationPageViewModel
{
    public IReadOnlyList<BotH2026EvaluationViewModel> Items { get; init; } = [];
    public long TotalRows { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public DateTime AsOfUtc { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public sealed class BotH2026EvaluationViewModel
{
    public long ShadowEvaluationId { get; init; }
    public long SourceEvaluationId { get; init; }
    public string CaptureKey { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public string BotKey { get; init; } = "H2026";
    public string AutomationVersion { get; init; } = string.Empty;
    public long PartidoProximoCuotaId { get; init; }
    public long OddsSnapshotId { get; init; }
    public DateTime OddsCapturedAtUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public DateTime SourceMatchDate { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string SourceMarketType { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public decimal LineValue { get; init; }
    public string Selection { get; init; } = string.Empty;
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal SelectedOdds { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionEngineType { get; init; } = string.Empty;
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string? BaseModelName { get; init; }
    public string? BaseModelVersion { get; init; }
    public DateTime? BaseModelTrainedThroughUtc { get; init; }
    public double? BaseRawProbability { get; init; }
    public double? BaseCalibratedProbability { get; init; }
    public double? RawImpliedProbability { get; init; }
    public double? MarketNoVigProbability { get; init; }
    public double? FinalProbability { get; init; }
    public double? FinalEdge { get; init; }
    public double? FinalExpectedValue { get; init; }
    public double? SelectionScore { get; init; }
    public double? ContextAgreementScore { get; init; }
    public double? DataQualityScore { get; init; }
    public decimal VirtualStakeUnits { get; init; }
    public string DecisionReasonsJson { get; init; } = "[]";
    public string RiskFlagsJson { get; init; } = "[]";
    public string Explanation { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";
    public string SnapshotLineageState { get; init; } = string.Empty;
    public int MatchCandidateCount { get; init; }
    public long? MatchHistoryId { get; init; }
    public string? MatchLinkMethod { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public int? ActualHomeCorners { get; init; }
    public int? ActualAwayCorners { get; init; }
    public int? ActualValue { get; init; }
    public string SettlementState { get; init; } = string.Empty;
    public decimal? SettlementFactor { get; init; }
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public double? EconomicOutcome { get; init; }
    public DateTime CapturedAtUtc { get; init; }
    public long TotalRows { get; init; }
}

public sealed class BotH2026ScorecardViewModel
{
    public int WindowDays { get; init; }
    public DateTime DateFromUtc { get; init; }
    public DateTime DateToUtc { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public string? ConfigurationVersion { get; init; }
    public string? MarketType { get; init; }
    public string? Selection { get; init; }
    public long Evaluations { get; init; }
    public long FixturesEvaluated { get; init; }
    public long ApprovedSignals { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long SafelySettled { get; init; }
    public long UnsafeOrUnavailable { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? ObservedEconomicOutcome { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? AverageEdge { get; init; }
    public double? AverageExpectedValue { get; init; }
    public double? CoverageRate { get; init; }
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = "SHADOW_ONLY";
    public string UnitOfAnalysis { get; init; } = "FIRST_APPROVED_PER_FIXTURE_CONFIGURATION";
}

public sealed class BotH2026ThresholdAnalysisViewModel
{
    public string AnalysisVersion { get; init; } = "bot-h-threshold-what-if-1.0.0";
    public DateTime AsOfUtc { get; init; }
    public string? ConfigurationVersion { get; init; }
    public string? MarketType { get; init; }
    public string? Selection { get; init; }
    public decimal MinimumFinalProbability { get; init; }
    public decimal MinimumFinalEdge { get; init; }
    public decimal MinimumFinalExpectedValue { get; init; }
    public decimal MinimumDataQualityScore { get; init; }
    public decimal MinimumContextAgreementScore { get; init; }
    public decimal MinimumOdds { get; init; }
    public decimal MaximumOdds { get; init; }
    public decimal DevelopmentFraction { get; init; }
    public DateTime? SplitBoundaryUtc { get; init; }
    public string Split { get; init; } = string.Empty;
    public long AvailableSettledEvaluations { get; init; }
    public long EligibleEvaluations { get; init; }
    public long SelectedPicks { get; init; }
    public long Fixtures { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageOdds { get; init; }
    public double? AverageModelProbability { get; init; }
    public double? AverageMarketProbability { get; init; }
    public double? AverageEdge { get; init; }
    public double? AverageExpectedValue { get; init; }
    public double? ObservedEconomicOutcome { get; init; }
    public double? CalibrationGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public bool ReadOnly { get; init; } = true;
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = "SHADOW_ONLY";
    public string UnitOfAnalysis { get; init; } = "FIRST_ELIGIBLE_PER_FIXTURE";
}
