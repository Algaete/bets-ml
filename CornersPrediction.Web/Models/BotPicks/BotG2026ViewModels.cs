namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotG2026FiltersViewModel
{
    public DateTime? DateFromUtc { get; set; }
    public DateTime? DateToUtc { get; set; }
    public string? Decision { get; set; }
    public string? PublicationStatus { get; set; }
    public string? MarketType { get; set; }
    public string? Selection { get; set; }
    public string? Bookmaker { get; set; }
    public string? ConfigurationVersion { get; set; }
    public string? Result { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class BotG2026IndexViewModel
{
    public BotG2026FiltersViewModel Filters { get; init; } = new();
    public BotG2026CandidatePageViewModel Candidates { get; init; } = new();
    public IReadOnlyList<BotG2026ScorecardViewModel> Scorecards { get; init; } = [];
    public BotG2026RuntimeStatusViewModel RuntimeStatus { get; init; } = new();
    public string? CandidatesErrorMessage { get; init; }
    public string? ScorecardsErrorMessage { get; init; }
    public string? RuntimeStatusErrorMessage { get; init; }

    public bool CandidatesAvailable => string.IsNullOrWhiteSpace(CandidatesErrorMessage);
    public bool ScorecardsAvailable => string.IsNullOrWhiteSpace(ScorecardsErrorMessage);
    public bool RuntimeStatusAvailable => string.IsNullOrWhiteSpace(RuntimeStatusErrorMessage);
}

public sealed class BotG2026RuntimeStatusViewModel
{
    public bool Enabled { get; init; }
    public bool Available { get; init; }
    public string State { get; init; } = "Unknown";
    public string Message { get; init; } = "No se pudo determinar el estado del artefacto.";
    public string? ModelVersion { get; init; }
    public string? ConfigurationVersion { get; init; }
    public string? FeatureSchemaVersion { get; init; }
    public DateTime? TrainedThroughUtc { get; init; }
}

public sealed class BotG2026CandidatePageViewModel
{
    public IReadOnlyList<BotG2026CandidateViewModel> Items { get; init; } = [];
    public long TotalRows { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public sealed class BotG2026CandidateViewModel
{
    public long CandidateId { get; init; }
    public Guid CandidateUuid { get; init; }
    public long FixtureId { get; init; }
    public long? OfficialFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public DateTime OddsTimestampUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Bookmaker { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public decimal Line { get; init; }
    public string Selection { get; init; } = string.Empty;
    public decimal? OverOdds { get; init; }
    public decimal? UnderOdds { get; init; }
    public decimal SelectedOdds { get; init; }
    public double? RawImpliedProbability { get; init; }
    public double? MarketNoVigProbability { get; init; }
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
    public double? UncertaintyScore { get; init; }
    public double? CalibrationReliability { get; init; }
    public double? OutOfDistributionScore { get; init; }
    public double? GSelectionScore { get; init; }
    public string Decision { get; init; } = string.Empty;
    public string DecisionReason { get; init; } = string.Empty;
    public string DecisionReasonsJson { get; init; } = "[]";
    public bool Approved { get; init; }
    public bool Published { get; init; }
    public string PublicationStatus { get; init; } = "Shadow";
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public string? BaseModelVersion { get; init; }
    public string? MetaModelVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public string? UncertaintyVersion { get; init; }
    public string? OodVersion { get; init; }
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public decimal? ClosingLine { get; init; }
    public decimal? ClosingOdds { get; init; }
    public string FeatureSnapshotJson { get; init; } = "{}";
}

public sealed class BotG2026ScorecardViewModel
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
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? HitRate { get; init; }
    public double? AverageOdds { get; init; }
    public double? AverageRawEdge { get; init; }
    public double? AverageConservativeEdge { get; init; }
    public double? AverageRawExpectedValue { get; init; }
    public double? AverageConservativeExpectedValue { get; init; }
    public double? ExpectedValueYieldGap { get; init; }
    public double? Brier { get; init; }
    public double? MarketBrier { get; init; }
    public double? DeltaBrier { get; init; }
    public double? LogLoss { get; init; }
    public double? MarketLogLoss { get; init; }
    public double? DeltaLogLoss { get; init; }
    public double? Ece { get; init; }
    public double? MaximumDrawdown { get; init; }
    public double? ProfitFactor { get; init; }
    public double? CoverageRate { get; init; }
    public double? PublicationRate { get; init; }
    public double? AverageOddsClv { get; init; }
    public double? AverageLineClv { get; init; }
    public string SuggestedPromotionStage { get; init; } = "SHADOW";
}
