namespace CornersPrediction.Domain.FootballIntelligence;

public enum NewsSourceTier
{
    Official = 1,
    StructuredProvider = 2,
    MajorMedia = 3,
    LocalJournalist = 4,
    Aggregator = 5,
    Unknown = 6,
    Rumor = 7
}

public enum FootballNewsEventType
{
    Injury,
    Illness,
    Suspension,
    Doubt,
    Return,
    TrainingReturn,
    Rest,
    Rotation,
    TacticalExclusion,
    NotCalled,
    CalledUp,
    TravelIssue,
    Fatigue,
    CoachChange,
    TacticalChange,
    FormationChange,
    ExpectedStarter,
    ExpectedBench,
    OfficialStarter,
    OfficialBench,
    SetPieceChange,
    InternalIssue,
    MoralePositive,
    MoraleNegative,
    Unknown
}

public enum AvailabilityStatus
{
    Unknown,
    Available,
    ExpectedAvailable,
    Doubtful,
    ExpectedOut,
    ConfirmedOut,
    Suspended,
    Rested,
    NotCalled,
    Starting,
    Bench
}

public enum FactCertainty
{
    Confirmed,
    Reported,
    Expected,
    Speculation,
    Rumor
}

public enum EntityResolutionStatus
{
    ResolvedExact,
    ResolvedAlias,
    ResolvedFuzzy,
    Ambiguous,
    NotFound
}

public enum PreMatchDecision
{
    Keep,
    WaitLineup,
    Recalculate,
    ReduceConfidence,
    Reject
}

public enum NewsExtractionStatus
{
    Pending,
    Extracted,
    Irrelevant,
    Failed
}

public enum MatchIntelligenceRunStatus
{
    Pending,
    Running,
    Completed,
    CompletedWithoutEvidence,
    Failed
}

public enum IntelligenceEvidenceStatus
{
    Missing,
    NoActionableFacts,
    LowConfidence,
    Stale,
    FutureCutoff,
    Available
}
