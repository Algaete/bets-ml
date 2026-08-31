namespace CornersPrediction.Domain.FootballIntelligence;

public sealed record FootballNewsDocument
{
    public long Id { get; init; }
    public long FixtureId { get; init; }
    public int? TeamId { get; init; }
    public required string Url { get; init; }
    public string? CanonicalUrl { get; init; }
    public required string UrlHash { get; init; }
    public string? ContentHash { get; init; }
    public required string SourceDomain { get; init; }
    public NewsSourceTier SourceTier { get; init; }
    public required string Title { get; init; }
    public string? Author { get; init; }
    public string? LanguageCode { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public DateTime FirstSeenAtUtc { get; init; }
    public DateTime RetrievedAtUtc { get; init; }
    public string? NormalizedText { get; init; }
    public NewsExtractionStatus ExtractionStatus { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record FootballNewsFact
{
    public long Id { get; init; }
    public long NewsDocumentId { get; init; }
    public required string FactHash { get; init; }
    public long FixtureId { get; init; }
    public int? TeamId { get; init; }
    public int? PlayerId { get; init; }
    public required string TeamNameExtracted { get; init; }
    public string? PlayerNameExtracted { get; init; }
    public string? PositionCode { get; init; }
    public FootballNewsEventType EventType { get; init; }
    public AvailabilityStatus AvailabilityStatus { get; init; }
    public FactCertainty Certainty { get; init; }
    public decimal? ProbabilityAvailable { get; init; }
    public decimal? ExpectedMinutesDelta { get; init; }
    public string? Reason { get; init; }
    public required string EvidenceSnippet { get; init; }
    public DateTime? EventEffectiveAtUtc { get; init; }
    public DateTime? ExpectedReturnAtUtc { get; init; }
    public decimal FixtureRelevance { get; init; }
    public decimal ExtractionConfidence { get; init; }
    public decimal SourceConfidence { get; init; }
    public decimal EffectiveConfidence { get; init; }
    public EntityResolutionStatus ResolutionStatus { get; init; }
    public bool IsCurrent { get; init; }
    public long? SupersededByFactId { get; init; }
    public required string ExtractionModel { get; init; }
    public required string PromptVersion { get; init; }
    public bool IsCurrentExtraction { get; init; }
    public DateTime FirstSeenAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record MatchTeamIntelligenceSnapshot
{
    public long Id { get; init; }
    public long FixtureId { get; init; }
    public int TeamId { get; init; }
    public bool IsHomeTeam { get; init; }
    public DateTime CutoffAtUtc { get; init; }
    public DateTime KickoffAtUtc { get; init; }
    public int DocumentCount { get; init; }
    public int IndependentSourceCount { get; init; }
    public int ActionableFactCount { get; init; }
    public int StructuredEvidenceCount { get; init; }
    public int ConfirmedOutCount { get; init; }
    public int DoubtfulCount { get; init; }
    public int SuspendedCount { get; init; }
    public decimal MissingStarterMinutesPct { get; init; }
    public decimal MissingAttackMinutesPct { get; init; }
    public decimal MissingMidfieldMinutesPct { get; init; }
    public decimal MissingDefenceMinutesPct { get; init; }
    public decimal AttackAvailabilityImpact { get; init; }
    public decimal MidfieldAvailabilityImpact { get; init; }
    public decimal DefenceAvailabilityImpact { get; init; }
    public decimal GoalkeeperAvailabilityImpact { get; init; }
    public decimal WidthAvailabilityImpact { get; init; }
    public decimal SetPieceAvailabilityImpact { get; init; }
    public decimal RotationRisk { get; init; }
    public decimal FatigueRisk { get; init; }
    public decimal MoraleSignal { get; init; }
    public int? CoachChangeDays { get; init; }
    public string? ExpectedFormation { get; init; }
    public bool FormationChangeExpected { get; init; }
    public bool OfficialLineupAvailable { get; init; }
    public int ExpectedXiChanges { get; init; }
    public decimal OverallNewsConfidence { get; init; }
    public int ConflictCount { get; init; }
    public int SnapshotAgeMinutes { get; init; }
    public decimal MissingWingerMinutesPct { get; init; }
    public decimal MissingFullBackMinutesPct { get; init; }
    public decimal MissingCornerTakerShare { get; init; }
    public decimal MissingCrossShare { get; init; }
    public decimal CornerCreationImpact { get; init; }
    public decimal MissingShotShare { get; init; }
    public decimal MissingCreatorShare { get; init; }
    public decimal ShotGenerationImpact { get; init; }
    public decimal MissingSotShare { get; init; }
    public decimal FinishingAvailabilityImpact { get; init; }
    public decimal MissingGoalShare { get; init; }
    public bool PenaltyTakerMissing { get; init; }
    public decimal GoalScoringAvailabilityImpact { get; init; }
    public required string RiskFlagsJson { get; init; }
    public required string DetailJson { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed record MatchIntelligenceRun
{
    public long Id { get; init; }
    public long FixtureId { get; init; }
    public DateTime CutoffAtUtc { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; init; }
    public MatchIntelligenceRunStatus Status { get; init; }
    public int QueriesGenerated { get; init; }
    public int SearchResults { get; init; }
    public int DocumentsDownloaded { get; init; }
    public int DocumentsProcessed { get; init; }
    public int FactsExtracted { get; init; }
    public int ResolvedFacts { get; init; }
    public int UnresolvedFacts { get; init; }
    public int ConflictCount { get; init; }
    public decimal ApiCost { get; init; }
    public int LlmTokensInput { get; init; }
    public int LlmTokensOutput { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
