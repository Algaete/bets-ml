namespace CornersPrediction.Domain.Automation.BotG;

public enum BotGMarketType
{
    TotalGoals,
    HomeTeamGoals,
    AwayTeamGoals
}

public enum BotGSelection
{
    Over,
    Under
}

public enum BotGDecisionStatus
{
    Approved,
    Rejected,
    Abstain
}

public enum BotGDecisionReason
{
    Approved,
    InvalidInput,
    UnsupportedMarket,
    UnsupportedLine,
    NoVigUnavailable,
    InvalidOdds,
    StaleOdds,
    InsufficientHistory,
    InsufficientCalibrationEvidence,
    CalibrationUnreliable,
    ModelUnavailable,
    SettlementDistributionUnavailable,
    ModelSchemaMismatch,
    ModelTemporalLeakage,
    FeatureTemporalLeakage,
    HighUncertainty,
    OutOfDistribution,
    LowDataQuality,
    ModelDisagreement,
    PredictionMonotonicityViolation,
    OddsOutOfRange,
    LowFinalProbability,
    LowConservativeEdge,
    LowConservativeExpectedValue,
    LowerRankedCandidate
}

public enum BotGSettlementState
{
    Win,
    HalfWin,
    Push,
    HalfLoss,
    Loss,
    Void,
    Pending
}

public enum BotGCalibrationLevel
{
    GlobalGoals,
    MarketType,
    MarketTypeAndSelection,
    MarketTypeSelectionAndBookmaker
}
