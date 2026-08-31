namespace CornersPrediction.Web.Models.BotPicks;

public sealed class BotI2026FiltersViewModel
{
    public DateTime? PredictionFromUtc { get; set; }
    public DateTime? PredictionToUtc { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Decision { get; set; }
    public string? MarketType { get; set; }
    public string? Selection { get; set; }
    public string? Source { get; set; }
    public string? ConfigurationVersion { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class BotI2026CollectViewModel
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public int MaximumFixtures { get; set; } = 50;
}

public sealed class BotI2026IndexViewModel
{
    public BotI2026FiltersViewModel Filters { get; init; } = new();
    public BotI2026CollectViewModel Collection { get; init; } = new();
    public BotI2026StatusViewModel Status { get; init; } = new();
    public BotI2026EvaluationPageViewModel Evaluations { get; init; } = new();
    public IReadOnlyList<BotI2026ScorecardViewModel> Scorecards { get; init; } = [];
    public string? StatusErrorMessage { get; init; }
    public string? EvaluationsErrorMessage { get; init; }
    public string? ScorecardsErrorMessage { get; init; }

    public bool StatusAvailable => string.IsNullOrWhiteSpace(StatusErrorMessage);
    public bool EvaluationsAvailable => string.IsNullOrWhiteSpace(EvaluationsErrorMessage);
    public bool ScorecardsAvailable => string.IsNullOrWhiteSpace(ScorecardsErrorMessage);
}

public sealed class BotI2026StatusViewModel
{
    public string BotKey { get; init; } = "I2026";
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public bool SchemaReady { get; init; }
    public bool ShadowOnly { get; init; } = true;
    public bool PublicationBlocked { get; init; } = true;
    public long Evaluations { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long Abstained { get; init; }
    public long UnsafeRows { get; init; }
    public DateTime? FirstPredictionTimestampUtc { get; init; }
    public DateTime? LastPredictionTimestampUtc { get; init; }
    public string State { get; init; } = "SHADOW_ONLY";
}

public sealed class BotI2026CollectResultViewModel
{
    public int TimelinesLoaded { get; init; }
    public int Inserted { get; init; }
    public int AlreadyCaptured { get; init; }
    public int Approved { get; init; }
    public int Rejected { get; init; }
    public int Abstained { get; init; }
    public DateTime AsOfUtc { get; init; }
    public bool ShadowOnly { get; init; } = true;
    public bool PublicationBlocked { get; init; } = true;
}

public sealed class BotI2026EvaluationPageViewModel
{
    public IReadOnlyList<BotI2026EvaluationViewModel> Items { get; init; } = [];
    public long TotalRows { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public DateTime AsOfUtc { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public sealed class BotI2026EvaluationViewModel
{
    public long ShadowEvaluationId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public string BotKey { get; init; } = "I2026";
    public string ConfigurationVersion { get; init; } = string.Empty;
    public string FeatureSchemaVersion { get; init; } = string.Empty;
    public long FixtureIdentity { get; init; }
    public long? ApiFootballFixtureId { get; init; }
    public DateTime FixtureDateUtc { get; init; }
    public DateTime PredictionTimestampUtc { get; init; }
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? SourceMatchId { get; init; }
    public string MarketType { get; init; } = string.Empty;
    public string Selection { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public decimal SignalScore { get; init; }
    public decimal SelectedOdds { get; init; }
    public long OpeningSnapshotId { get; init; }
    public long CurrentSnapshotId { get; init; }
    public long? PeerSnapshotId { get; init; }
    public DateTime OpeningCapturedAtUtc { get; init; }
    public DateTime CurrentCapturedAtUtc { get; init; }
    public DateTime? PeerCapturedAtUtc { get; init; }
    public decimal OpeningLine { get; init; }
    public decimal CurrentLine { get; init; }
    public decimal? PeerLine { get; init; }
    public decimal OpeningOverNoVigProbability { get; init; }
    public decimal CurrentOverNoVigProbability { get; init; }
    public decimal? PeerOverNoVigProbability { get; init; }
    public decimal SelectedProbabilityMovement { get; init; }
    public decimal SelectedLineMovement { get; init; }
    public decimal MovementVelocityPerHour { get; init; }
    public decimal ObservationHours { get; init; }
    public decimal OddsAgeMinutes { get; init; }
    public int SnapshotCount { get; init; }
    public string? PeerSource { get; init; }
    public decimal? PinnacleOverNoVigProbability { get; init; }
    public decimal? BetanoOverNoVigProbability { get; init; }
    public decimal? CrossBookProbabilityDispersion { get; init; }
    public decimal? CrossBookLineDispersion { get; init; }
    public string ReasonCodesJson { get; init; } = "[]";
    public string RiskFlagsJson { get; init; } = "[]";
    public string Explanation { get; init; } = string.Empty;
    public string FeatureSnapshotJson { get; init; } = "{}";
    public bool ShadowOnly { get; init; }
    public bool PublicationBlocked { get; init; }
    public long MatchCandidateCount { get; init; }
    public long? MatchHistoryId { get; init; }
    public DateTime? OutcomeAvailableUtc { get; init; }
    public int? ActualValue { get; init; }
    public string SettlementState { get; init; } = string.Empty;
    public decimal? SettlementFactor { get; init; }
    public string? Result { get; init; }
    public decimal? ProfitLoss { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public long TotalRows { get; init; }
}

public sealed class BotI2026ScorecardViewModel
{
    public int WindowDays { get; init; }
    public DateTime DateFromUtc { get; init; }
    public DateTime DateToUtc { get; init; }
    public string Dimension { get; init; } = string.Empty;
    public string Segment { get; init; } = string.Empty;
    public long Evaluations { get; init; }
    public long FixturesEvaluated { get; init; }
    public long Approved { get; init; }
    public long Rejected { get; init; }
    public long Abstained { get; init; }
    public long Settled { get; init; }
    public long Won { get; init; }
    public long HalfWon { get; init; }
    public long Pushes { get; init; }
    public long HalfLost { get; init; }
    public long Lost { get; init; }
    public double? ApprovalRate { get; init; }
    public double? CrossBookCoverageRate { get; init; }
    public double? Stake { get; init; }
    public double? ProfitLoss { get; init; }
    public double? Yield { get; init; }
    public double? AverageSignalScore { get; init; }
    public double? AverageAbsoluteProbabilityMovement { get; init; }
    public double? AverageAbsoluteLineMovement { get; init; }
    public double? AverageOddsAgeMinutes { get; init; }
    public double? AverageObservationHours { get; init; }
    public bool Deployable { get; init; }
    public string PromotionState { get; init; } = "SHADOW_ONLY";
    public string ScorecardType { get; init; } = "OUTCOME_AWARE_SHADOW_OFFICIAL_FIXTURE_ONLY";
}
