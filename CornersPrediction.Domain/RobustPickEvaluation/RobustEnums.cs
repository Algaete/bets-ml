namespace CornersPrediction.Domain.RobustPickEvaluation;

public enum MarketFamily
{
    Goals,
    Corners,
    Shots,
    ShotsOnGoal
}

public enum MarketScope
{
    Total,
    Home,
    Away
}

public enum SelectionSide
{
    Over,
    Under
}

public enum PredictionComponentType
{
    Direct,
    HomeAwaySum,
    Context,
    Reconciled,
    IntelligenceAdjusted,
    Scenario
}

public enum EvidenceStatus
{
    AppliedPositive,
    AppliedNegative,
    ReviewedNeutral,
    InsufficientEvidence,
    SourceUnavailable,
    SnapshotExpired,
    NotApplicable
}

public enum EvaluationMode
{
    Disabled,
    Shadow,
    Enforce
}

public enum CurrentSystemDecision
{
    NoBet,
    Bet
}

public enum RobustDecision
{
    Approve,
    Reject,
    ReduceStake,
    ManualReview
}

public enum SettlementOutcome
{
    Win,
    HalfWin,
    Push,
    HalfLoss,
    Loss
}

public enum ResidualFallbackLevel
{
    ExactMarketSideLeagueLineBand,
    ExactMarketSideLeague,
    ExactMarketSide,
    MarketFamilyScopeSide,
    MarketFamilyScope,
    MarketFamily,
    Unavailable
}

public enum ResidualSourceScope
{
    AllCandidates,
    SelectedPicksOnly
}

public enum ErrorScaleMethod
{
    RobustMad,
    WeightedRmse,
    WeightedMae,
    ConfiguredModelMae,
    Unavailable
}

public enum ReconciliationFallbackReason
{
    None,
    NoUsableComponents,
    DirectPredictionOnly,
    InsufficientOutOfSampleValidation,
    DirectPredictionUnavailable
}

public enum CalibrationFallbackLevel
{
    ExactMarket,
    MarketFamily,
    Global,
    Unavailable
}

public enum NoVigStatus
{
    Available,
    Unavailable
}

public enum OddsAvailabilityStatus
{
    Available,
    Stale,
    SourceUnavailable,
    SnapshotExpired
}

public enum RobustReasonCode
{
    RobustEdgeBelowMinimum,
    RobustEvNotPositive,
    PositiveEvStabilityTooLow,
    CalibrationReliabilityTooLow,
    ResidualSampleTooSmall,
    WorstCaseDistanceTooSmall,
    ConsensusRangeTooLarge,
    CoherenceGapTooLarge,
    SideDisagreement,
    OddsTooOld,
    MarketPriceUnavailable,
    NoVigUnavailable,
    LineupScenarioUnstable,
    DataQualityTooLow,
    ModelTrainedAfterFixture,
    LookaheadDataDetected,
    ExposureLimitExceeded,
    CorrelatedExposureLimitExceeded,
    MarketAutomationNameMismatch,
    IntelligenceSourceUnavailable,
    SnapshotExpired,
    ErrorScaleUnavailable,
    PointEdgeBelowMinimum,
    PointEvBelowMinimum,
    EvidenceInsufficient,
    RobustnessScoreTooLow
}

public static class RobustReasonCodes
{
    public const string RobustEdgeBelowMinimum = "ROBUST_EDGE_BELOW_MINIMUM";
    public const string RobustEvNotPositive = "ROBUST_EV_NOT_POSITIVE";
    public const string PositiveEvStabilityTooLow = "POSITIVE_EV_STABILITY_TOO_LOW";
    public const string CalibrationReliabilityTooLow = "CALIBRATION_RELIABILITY_TOO_LOW";
    public const string ResidualSampleTooSmall = "RESIDUAL_SAMPLE_TOO_SMALL";
    public const string WorstCaseDistanceTooSmall = "WORST_CASE_DISTANCE_TOO_SMALL";
    public const string ConsensusRangeTooLarge = "CONSENSUS_RANGE_TOO_LARGE";
    public const string CoherenceGapTooLarge = "COHERENCE_GAP_TOO_LARGE";
    public const string SideDisagreement = "SIDE_DISAGREEMENT";
    public const string OddsTooOld = "ODDS_TOO_OLD";
    public const string MarketPriceUnavailable = "MARKET_PRICE_UNAVAILABLE";
    public const string NoVigUnavailable = "NO_VIG_UNAVAILABLE";
    public const string LineupScenarioUnstable = "LINEUP_SCENARIO_UNSTABLE";
    public const string DataQualityTooLow = "DATA_QUALITY_TOO_LOW";
    public const string ModelTrainedAfterFixture = "MODEL_TRAINED_AFTER_FIXTURE";
    public const string LookaheadDataDetected = "LOOKAHEAD_DATA_DETECTED";
    public const string ExposureLimitExceeded = "EXPOSURE_LIMIT_EXCEEDED";
    public const string CorrelatedExposureLimitExceeded = "CORRELATED_EXPOSURE_LIMIT_EXCEEDED";
    public const string MarketAutomationNameMismatch = "MARKET_AUTOMATION_NAME_MISMATCH";
    public const string IntelligenceSourceUnavailable = "INTELLIGENCE_SOURCE_UNAVAILABLE";
    public const string SnapshotExpired = "SNAPSHOT_EXPIRED";
    public const string ErrorScaleUnavailable = "ERROR_SCALE_UNAVAILABLE";
    public const string PointEdgeBelowMinimum = "POINT_EDGE_BELOW_MINIMUM";
    public const string PointEvBelowMinimum = "POINT_EV_BELOW_MINIMUM";
    public const string EvidenceInsufficient = "EVIDENCE_INSUFFICIENT";
    public const string RobustnessScoreTooLow = "ROBUSTNESS_SCORE_TOO_LOW";

    public static string ToStableCode(this RobustReasonCode reason) => reason switch
    {
        RobustReasonCode.RobustEdgeBelowMinimum => RobustEdgeBelowMinimum,
        RobustReasonCode.RobustEvNotPositive => RobustEvNotPositive,
        RobustReasonCode.PositiveEvStabilityTooLow => PositiveEvStabilityTooLow,
        RobustReasonCode.CalibrationReliabilityTooLow => CalibrationReliabilityTooLow,
        RobustReasonCode.ResidualSampleTooSmall => ResidualSampleTooSmall,
        RobustReasonCode.WorstCaseDistanceTooSmall => WorstCaseDistanceTooSmall,
        RobustReasonCode.ConsensusRangeTooLarge => ConsensusRangeTooLarge,
        RobustReasonCode.CoherenceGapTooLarge => CoherenceGapTooLarge,
        RobustReasonCode.SideDisagreement => SideDisagreement,
        RobustReasonCode.OddsTooOld => OddsTooOld,
        RobustReasonCode.MarketPriceUnavailable => MarketPriceUnavailable,
        RobustReasonCode.NoVigUnavailable => NoVigUnavailable,
        RobustReasonCode.LineupScenarioUnstable => LineupScenarioUnstable,
        RobustReasonCode.DataQualityTooLow => DataQualityTooLow,
        RobustReasonCode.ModelTrainedAfterFixture => ModelTrainedAfterFixture,
        RobustReasonCode.LookaheadDataDetected => LookaheadDataDetected,
        RobustReasonCode.ExposureLimitExceeded => ExposureLimitExceeded,
        RobustReasonCode.CorrelatedExposureLimitExceeded => CorrelatedExposureLimitExceeded,
        RobustReasonCode.MarketAutomationNameMismatch => MarketAutomationNameMismatch,
        RobustReasonCode.IntelligenceSourceUnavailable => IntelligenceSourceUnavailable,
        RobustReasonCode.SnapshotExpired => SnapshotExpired,
        RobustReasonCode.ErrorScaleUnavailable => ErrorScaleUnavailable,
        RobustReasonCode.PointEdgeBelowMinimum => PointEdgeBelowMinimum,
        RobustReasonCode.PointEvBelowMinimum => PointEvBelowMinimum,
        RobustReasonCode.EvidenceInsufficient => EvidenceInsufficient,
        RobustReasonCode.RobustnessScoreTooLow => RobustnessScoreTooLow,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };
}
