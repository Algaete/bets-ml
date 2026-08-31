/*
    Robust Pick Evaluation persistence, v1.

    Additive and idempotent.  Existing candidate evidence remains in
    dbo.AutomatedBotPickEvaluations and immutable bookmaker evidence remains in
    dbo.CornerOddsSnapshots; this migration intentionally does not duplicate them.
    Robust evaluations and their components are immutable snapshots.  The only
    permitted update is the atomic IsCurrent 1 -> 0 transition when a newer
    snapshot supersedes an older one.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NULL
    THROW 52200, 'Robust Pick Evaluation requires AutomatedBotPickEvaluations.', 1;
IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NULL
    THROW 52201, 'Robust Pick Evaluation requires AutomatedCornerBetSelections.', 1;
IF OBJECT_ID(N'dbo.CornerOddsSnapshots', N'U') IS NULL
    THROW 52202, 'Robust Pick Evaluation requires CornerOddsSnapshots.', 1;

GO

-- Backfill preview has to retain legacy rows whose prediction timestamp is
-- missing, but COALESCE(PredictionTimestampUtc, EvaluatedAtUtc) prevents a
-- bounded date seek.  Two disjoint filtered indexes support the equivalent
-- UNION ALL ranges without indexing the large JSON evidence payload.
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_RobustBackfillPredictionTime'
)
    CREATE INDEX IX_AutomatedBotPickEvaluations_RobustBackfillPredictionTime
        ON dbo.AutomatedBotPickEvaluations
           (PredictionTimestampUtc, AutomatedBotPickEvaluationId)
        WHERE PredictionTimestampUtc IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_RobustBackfillFallbackTime'
)
    CREATE INDEX IX_AutomatedBotPickEvaluations_RobustBackfillFallbackTime
        ON dbo.AutomatedBotPickEvaluations
           (EvaluatedAtUtc, AutomatedBotPickEvaluationId)
        INCLUDE (PredictionTimestampUtc)
        WHERE PredictionTimestampUtc IS NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_RobustResidualHistory'
)
    CREATE INDEX IX_AutomatedBotPickEvaluations_RobustResidualHistory
        ON dbo.AutomatedBotPickEvaluations
           (MarketFamily, MarketType, SelectedSide, League, PredictionTimestampUtc DESC)
        INCLUDE
        (
            AutomatedBotPickEvaluationId, FixtureIdentity, ApiFootballFixtureId,
            PublishedSelectionId, BotKey, ConfigurationVersion, MatchDate,
            LineValue, SelectedOdds, Prediction2026, LegacyPrediction,
            BaseModelTrainedThroughUtc, BaseModelVersion, DataQualityScore
        )
        WHERE PredictionTimestampUtc IS NOT NULL;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedBotPickRobustEvaluations
    (
        RobustEvaluationId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedBotPickRobustEvaluations PRIMARY KEY,
        LogicalPickKey CHAR(64) NOT NULL,
        IdempotencyHash CHAR(64) NOT NULL,
        InputHash CHAR(64) NOT NULL,
        SnapshotHash CHAR(64) NOT NULL,
        SourceEvaluationId BIGINT NULL,
        BotPickSelectionId BIGINT NULL,
        SourceOddsSnapshotId BIGINT NULL,
        FixtureId BIGINT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        MarketFamily NVARCHAR(30) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        Side NVARCHAR(10) NOT NULL,
        Line DECIMAL(10,2) NOT NULL,
        Odds DECIMAL(18,6) NOT NULL,
        Bookmaker NVARCHAR(50) NOT NULL,
        EvaluationSequence INT NOT NULL,
        EvaluationVersion NVARCHAR(80) NOT NULL,
        AsOfUtc DATETIME2(3) NOT NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_RobustEvaluations_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        IsCurrent BIT NOT NULL,
        SupersedesEvaluationId BIGINT NULL,

        BaseModelVersion NVARCHAR(120) NULL,
        ModelTrainedThroughUtc DATETIME2(3) NULL,
        SelectorVersion NVARCHAR(120) NULL,
        CalibrationVersion NVARCHAR(120) NULL,
        IntelligenceVersion NVARCHAR(120) NULL,
        SettlementVersion NVARCHAR(120) NULL,
        RobustnessVersion NVARCHAR(120) NOT NULL,
        PolicyVersion NVARCHAR(120) NOT NULL,

        DirectPrediction DECIMAL(18,6) NULL,
        HomePrediction DECIMAL(18,6) NULL,
        AwayPrediction DECIMAL(18,6) NULL,
        ComponentsPrediction DECIMAL(18,6) NULL,
        ContextPrediction DECIMAL(18,6) NULL,
        ReconciledPrediction DECIMAL(18,6) NULL,
        ConsensusMinimum DECIMAL(18,6) NULL,
        ConsensusMaximum DECIMAL(18,6) NULL,
        ConsensusRange DECIMAL(18,6) NULL,
        CoherenceGap DECIMAL(18,6) NULL,

        DirectDistance DECIMAL(18,6) NULL,
        ComponentsDistance DECIMAL(18,6) NULL,
        ContextDistance DECIMAL(18,6) NULL,
        ReconciledDistance DECIMAL(18,6) NULL,
        WorstCasePrediction DECIMAL(18,6) NULL,
        WorstCaseDistance DECIMAL(18,6) NULL,
        ErrorScale DECIMAL(18,6) NULL,
        NormalizedDirectDistance DECIMAL(18,6) NULL,
        NormalizedWorstCaseDistance DECIMAL(18,6) NULL,
        NormalizedConsensusRange DECIMAL(18,6) NULL,
        NormalizedCoherenceGap DECIMAL(18,6) NULL,

        SideAgreement BIT NULL,
        MagnitudeAgreementScore DECIMAL(9,6) NULL,
        ProbabilityAgreementScore DECIMAL(9,6) NULL,
        CoherenceScore DECIMAL(9,6) NULL,
        ScenarioSideStability DECIMAL(9,6) NULL,
        PositiveEvStability DECIMAL(9,6) NULL,

        P01 DECIMAL(18,6) NULL,
        P05 DECIMAL(18,6) NULL,
        P10 DECIMAL(18,6) NULL,
        P25 DECIMAL(18,6) NULL,
        P50 DECIMAL(18,6) NULL,
        P75 DECIMAL(18,6) NULL,
        P90 DECIMAL(18,6) NULL,
        P95 DECIMAL(18,6) NULL,
        P99 DECIMAL(18,6) NULL,
        DistributionMean DECIMAL(18,6) NULL,
        StandardDeviation DECIMAL(18,6) NULL,
        MedianAbsoluteDeviation DECIMAL(18,6) NULL,
        DistributionEffectiveN DECIMAL(18,6) NULL,
        ResidualRawObservationCount INT NULL,
        SimulationCount INT NULL,
        DistributionMethod NVARCHAR(80) NULL,
        DistributionVersion NVARCHAR(120) NULL,
        HistogramJson NVARCHAR(MAX) NOT NULL,
        PWin DECIMAL(9,6) NULL,
        PHalfWin DECIMAL(9,6) NULL,
        PPush DECIMAL(9,6) NULL,
        PHalfLoss DECIMAL(9,6) NULL,
        PLoss DECIMAL(9,6) NULL,

        RawProbability DECIMAL(9,6) NULL,
        CalibratedProbability DECIMAL(9,6) NULL,
        ProbabilityLowerBound DECIMAL(9,6) NULL,
        ProbabilityUpperBound DECIMAL(9,6) NULL,
        ModelFairOdds DECIMAL(18,6) NULL,
        ModelFairProbability DECIMAL(9,6) NULL,
        RobustModelFairProbability DECIMAL(9,6) NULL,
        MarketImpliedProbability DECIMAL(9,6) NULL,
        MarketNoVigProbability DECIMAL(9,6) NULL,
        ConservativeMarketProbability DECIMAL(9,6) NULL,

        PointEdge DECIMAL(12,6) NULL,
        RobustEdge DECIMAL(12,6) NULL,
        PointExpectedValue DECIMAL(12,6) NULL,
        RobustExpectedValue DECIMAL(12,6) NULL,
        ExpectedValueP10 DECIMAL(12,6) NULL,
        ExpectedValueP50 DECIMAL(12,6) NULL,
        ExpectedValueP90 DECIMAL(12,6) NULL,
        EdgeP10 DECIMAL(12,6) NULL,
        EdgeP50 DECIMAL(12,6) NULL,
        EdgeP90 DECIMAL(12,6) NULL,

        CalibrationEffectiveN DECIMAL(18,6) NULL,
        CalibrationExactMarketN INT NULL,
        CalibrationFamilyN INT NULL,
        CalibrationGlobalN INT NULL,
        CalibrationReliability DECIMAL(9,6) NULL,
        CalibrationSpecificityScore DECIMAL(9,6) NULL,
        CalibrationRecencyScore DECIMAL(9,6) NULL,
        CalibrationErrorScore DECIMAL(9,6) NULL,
        CalibrationFallbackLevel NVARCHAR(80) NULL,

        OddsEvaluated DECIMAL(18,6) NULL,
        OddsTaken DECIMAL(18,6) NULL,
        OpeningOdds DECIMAL(18,6) NULL,
        ClosingOdds DECIMAL(18,6) NULL,
        BestAvailableOdds DECIMAL(18,6) NULL,
        MedianMarketOdds DECIMAL(18,6) NULL,
        QuoteTimestampUtc DATETIME2(3) NULL,
        OddsAgeSeconds INT NULL,
        MinutesToKickoff INT NULL,
        NoVigMethod NVARCHAR(40) NULL,
        OddsReliability DECIMAL(9,6) NULL,
        OpeningLine DECIMAL(10,2) NULL,
        ClosingLine DECIMAL(10,2) NULL,
        ClvOdds DECIMAL(12,6) NULL,
        ClvLine DECIMAL(12,6) NULL,

        LineupStatus NVARCHAR(40) NULL,
        IntelligenceEvidenceStatus NVARCHAR(40) NULL,
        FatigueDataStatus NVARCHAR(40) NULL,
        GameStateModelStatus NVARCHAR(40) NULL,
        ScenarioCount INT NULL,
        AdverseScenarioProbability DECIMAL(9,6) NULL,
        ScenarioStability DECIMAL(9,6) NULL,

        EvaluationMode NVARCHAR(20) NOT NULL,
        CurrentSystemDecision NVARCHAR(30) NOT NULL,
        RobustDecision NVARCHAR(30) NOT NULL,
        OriginalStake DECIMAL(12,4) NOT NULL,
        RecommendedStake DECIMAL(12,4) NOT NULL,
        StakeMultiplier DECIMAL(9,6) NOT NULL,
        RobustnessScore DECIMAL(9,6) NULL,
        RejectionReasonCodesJson NVARCHAR(MAX) NOT NULL,
        WarningCodesJson NVARCHAR(MAX) NOT NULL,
        HumanReadableReason NVARCHAR(2000) NOT NULL,
        InputPayloadJson NVARCHAR(MAX) NOT NULL,
        EvaluationPayloadJson NVARCHAR(MAX) NOT NULL,

        CONSTRAINT UQ_RobustEvaluations_Idempotency UNIQUE (IdempotencyHash),
        CONSTRAINT FK_RobustEvaluations_SourceEvaluation FOREIGN KEY (SourceEvaluationId)
            REFERENCES dbo.AutomatedBotPickEvaluations(AutomatedBotPickEvaluationId),
        CONSTRAINT FK_RobustEvaluations_Selection FOREIGN KEY (BotPickSelectionId)
            REFERENCES dbo.AutomatedCornerBetSelections(AutomatedCornerBetSelectionId),
        CONSTRAINT FK_RobustEvaluations_OddsSnapshot FOREIGN KEY (SourceOddsSnapshotId)
            REFERENCES dbo.CornerOddsSnapshots(CornerOddsSnapshotId),
        CONSTRAINT FK_RobustEvaluations_Supersedes FOREIGN KEY (SupersedesEvaluationId)
            REFERENCES dbo.AutomatedBotPickRobustEvaluations(RobustEvaluationId),
        CONSTRAINT CK_RobustEvaluations_HashLengths CHECK
            (LEN(LogicalPickKey) = 64 AND LEN(IdempotencyHash) = 64
             AND LEN(InputHash) = 64 AND LEN(SnapshotHash) = 64),
        CONSTRAINT CK_RobustEvaluations_Sequence CHECK (EvaluationSequence > 0),
        CONSTRAINT CK_RobustEvaluations_Side CHECK (Side IN (N'Over', N'Under')),
        CONSTRAINT CK_RobustEvaluations_Odds CHECK (Odds > 1),
        CONSTRAINT CK_RobustEvaluations_TemporalSelf CHECK
            ((ModelTrainedThroughUtc IS NULL OR ModelTrainedThroughUtc <= AsOfUtc)
             AND (QuoteTimestampUtc IS NULL OR QuoteTimestampUtc <= AsOfUtc)),
        CONSTRAINT CK_RobustEvaluations_Mode CHECK
            (EvaluationMode IN (N'Shadow', N'Enforce', N'Disabled')),
        CONSTRAINT CK_RobustEvaluations_Decision CHECK
            (RobustDecision IN (N'Approve', N'Reject', N'ReduceStake', N'ManualReview')),
        CONSTRAINT CK_RobustEvaluations_Stake CHECK
            (OriginalStake >= 0 AND RecommendedStake >= 0
             AND RecommendedStake <= OriginalStake AND StakeMultiplier BETWEEN 0 AND 1),
        CONSTRAINT CK_RobustEvaluations_Json CHECK
            (ISJSON(HistogramJson) = 1
             AND ISJSON(RejectionReasonCodesJson) = 1
             AND ISJSON(WarningCodesJson) = 1
             AND ISJSON(InputPayloadJson) = 1
             AND ISJSON(EvaluationPayloadJson) = 1
             AND LEFT(LTRIM(HistogramJson), 1) IN (N'{', N'[')
             AND LEFT(LTRIM(RejectionReasonCodesJson), 1) = N'['
             AND LEFT(LTRIM(WarningCodesJson), 1) = N'['
             AND LEFT(LTRIM(InputPayloadJson), 1) = N'{'
             AND LEFT(LTRIM(EvaluationPayloadJson), 1) = N'{')
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'UX_RobustEvaluations_CurrentLogicalPick'
)
    CREATE UNIQUE INDEX UX_RobustEvaluations_CurrentLogicalPick
        ON dbo.AutomatedBotPickRobustEvaluations(LogicalPickKey)
        WHERE IsCurrent = 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'UX_RobustEvaluations_LogicalSequence'
)
    CREATE UNIQUE INDEX UX_RobustEvaluations_LogicalSequence
        ON dbo.AutomatedBotPickRobustEvaluations(LogicalPickKey, EvaluationSequence);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_SelectionCurrent'
)
    CREATE INDEX IX_RobustEvaluations_SelectionCurrent
        ON dbo.AutomatedBotPickRobustEvaluations(BotPickSelectionId, IsCurrent, AsOfUtc DESC)
        INCLUDE (RobustDecision, RecommendedStake, RobustnessScore, EvaluationVersion);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_SourceCurrent'
)
    CREATE INDEX IX_RobustEvaluations_SourceCurrent
        ON dbo.AutomatedBotPickRobustEvaluations(SourceEvaluationId, IsCurrent, AsOfUtc DESC);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_FixtureMarket'
)
    CREATE INDEX IX_RobustEvaluations_FixtureMarket
        ON dbo.AutomatedBotPickRobustEvaluations(FixtureId, MarketType, AsOfUtc DESC)
        INCLUDE (BotKey, MarketFamily, Side, Line, IsCurrent, RobustDecision);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_VersionMetrics'
)
    CREATE INDEX IX_RobustEvaluations_VersionMetrics
        ON dbo.AutomatedBotPickRobustEvaluations(EvaluationVersion, IsCurrent, AsOfUtc DESC)
        INCLUDE (BotKey, MarketFamily, MarketType, RobustDecision, PointEdge,
                 RobustEdge, PointExpectedValue, RobustExpectedValue, RobustnessScore);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_ModelVersionAsOf'
)
    CREATE INDEX IX_RobustEvaluations_ModelVersionAsOf
        ON dbo.AutomatedBotPickRobustEvaluations(BaseModelVersion, AsOfUtc DESC)
        INCLUDE (FixtureId, MarketFamily, MarketType, EvaluationVersion, IsCurrent);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations')
      AND name = N'IX_RobustEvaluations_CurrentAsOf'
)
    CREATE INDEX IX_RobustEvaluations_CurrentAsOf
        ON dbo.AutomatedBotPickRobustEvaluations(IsCurrent, AsOfUtc DESC)
        INCLUDE (BotKey, MarketFamily, MarketType, EvaluationVersion, RobustDecision);

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickRobustComponents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedBotPickRobustComponents
    (
        RobustComponentId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedBotPickRobustComponents PRIMARY KEY,
        RobustEvaluationId BIGINT NOT NULL,
        ComponentSequence INT NOT NULL,
        ComponentType NVARCHAR(40) NOT NULL,
        PredictedValue DECIMAL(18,6) NULL,
        ProbabilityForSelection DECIMAL(9,6) NULL,
        Weight DECIMAL(9,6) NOT NULL,
        IsUsable BIT NOT NULL,
        SourceVersion NVARCHAR(120) NULL,
        AsOfUtc DATETIME2(3) NOT NULL,
        ExclusionReason NVARCHAR(500) NULL,
        DataQualityScore DECIMAL(9,6) NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_RobustComponents_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_RobustComponents_Evaluation FOREIGN KEY (RobustEvaluationId)
            REFERENCES dbo.AutomatedBotPickRobustEvaluations(RobustEvaluationId),
        CONSTRAINT UQ_RobustComponents_EvaluationSequence
            UNIQUE (RobustEvaluationId, ComponentSequence),
        CONSTRAINT CK_RobustComponents_Sequence CHECK (ComponentSequence > 0),
        CONSTRAINT CK_RobustComponents_Weight CHECK (Weight BETWEEN 0 AND 1),
        CONSTRAINT CK_RobustComponents_MetadataJson CHECK (ISJSON(MetadataJson) = 1)
    );
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotRobustPolicies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedBotRobustPolicies
    (
        RobustPolicyId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedBotRobustPolicies PRIMARY KEY,
        PolicyHash CHAR(64) NOT NULL,
        PolicyVersion NVARCHAR(120) NOT NULL,
        EffectiveFromUtc DATETIME2(3) NOT NULL,
        EvaluationMode NVARCHAR(20) NOT NULL,
        BotKey NVARCHAR(50) NULL,
        MarketFamily NVARCHAR(30) NULL,
        MarketType NVARCHAR(50) NULL,
        MarketScope NVARCHAR(20) NULL,
        Side NVARCHAR(10) NULL,
        MinimumLine DECIMAL(10,2) NULL,
        MaximumLine DECIMAL(10,2) NULL,
        MinimumOdds DECIMAL(18,6) NULL,
        MaximumOdds DECIMAL(18,6) NULL,
        LeaguePattern NVARCHAR(200) NULL,
        ConfigurationJson NVARCHAR(MAX) NOT NULL,
        CreatedBy NVARCHAR(120) NOT NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_RobustPolicies_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_RobustPolicies_Hash UNIQUE (PolicyHash),
        CONSTRAINT CK_RobustPolicies_Mode CHECK
            (EvaluationMode IN (N'Shadow', N'Enforce', N'Disabled')),
        CONSTRAINT CK_RobustPolicies_Side CHECK
            (Side IS NULL OR Side IN (N'Over', N'Under')),
        CONSTRAINT CK_RobustPolicies_Ranges CHECK
            ((MinimumLine IS NULL OR MaximumLine IS NULL OR MinimumLine <= MaximumLine)
             AND (MinimumOdds IS NULL OR MaximumOdds IS NULL OR MinimumOdds <= MaximumOdds)
             AND (MinimumLine IS NULL OR MinimumLine >= 0)
             AND (MaximumLine IS NULL OR MaximumLine >= 0)
             AND (MinimumOdds IS NULL OR MinimumOdds > 1)
             AND (MaximumOdds IS NULL OR MaximumOdds > 1)),
        CONSTRAINT CK_RobustPolicies_Json CHECK
            (ISJSON(ConfigurationJson) = 1 AND LEFT(LTRIM(ConfigurationJson), 1) = N'{')
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotRobustPolicies')
      AND name = N'IX_RobustPolicies_EffectiveScope'
)
    CREATE INDEX IX_RobustPolicies_EffectiveScope
        ON dbo.AutomatedBotRobustPolicies
           (EffectiveFromUtc DESC, BotKey, MarketFamily, MarketType, MarketScope, Side)
        INCLUDE (PolicyVersion, EvaluationMode, MinimumLine, MaximumLine,
                 MinimumOdds, MaximumOdds, LeaguePattern);

GO

CREATE OR ALTER TRIGGER dbo.trg_RobustComponents_Immutable
ON dbo.AutomatedBotPickRobustComponents
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52203, 'Robust evaluation components are append-only.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_RobustComponents_TemporalGuard
ON dbo.AutomatedBotPickRobustComponents
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted AS component
        INNER JOIN dbo.AutomatedBotPickRobustEvaluations AS evaluation
          ON evaluation.RobustEvaluationId = component.RobustEvaluationId
        WHERE component.AsOfUtc > evaluation.AsOfUtc
    )
        THROW 52212, 'A prediction component cannot be newer than its robust evaluation.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_RobustEvaluations_TemporalGuard
ON dbo.AutomatedBotPickRobustEvaluations
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted AS evaluation
        LEFT JOIN dbo.CornerOddsSnapshots AS odds
          ON odds.CornerOddsSnapshotId = evaluation.SourceOddsSnapshotId
        LEFT JOIN dbo.AutomatedBotPickEvaluations AS sourceEvaluation
          ON sourceEvaluation.AutomatedBotPickEvaluationId = evaluation.SourceEvaluationId
        WHERE (odds.CapturedAtUtc IS NOT NULL AND odds.CapturedAtUtc > evaluation.AsOfUtc)
           OR (sourceEvaluation.PredictionTimestampUtc IS NOT NULL
               AND sourceEvaluation.PredictionTimestampUtc > evaluation.AsOfUtc)
    )
        THROW 52213, 'Robust evaluation lineage contains evidence newer than AsOfUtc.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_RobustPolicies_Immutable
ON dbo.AutomatedBotRobustPolicies
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52204, 'Robust policies are versioned append-only records.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_RobustEvaluations_AppendOnly
ON dbo.AutomatedBotPickRobustEvaluations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM deleted)
       AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 52205, 'Robust evaluations cannot be deleted.', 1;

    -- Supersession may only flip the current marker from 1 to 0.  Every evidence,
    -- metric and lineage column is immutable once inserted.
    IF NOT UPDATE(IsCurrent)
       OR UPDATE(LogicalPickKey) OR UPDATE(IdempotencyHash) OR UPDATE(InputHash)
       OR UPDATE(SnapshotHash) OR UPDATE(SourceEvaluationId) OR UPDATE(BotPickSelectionId)
       OR UPDATE(SourceOddsSnapshotId) OR UPDATE(FixtureId) OR UPDATE(BotKey)
       OR UPDATE(MarketFamily) OR UPDATE(MarketType) OR UPDATE(Side) OR UPDATE(Line)
       OR UPDATE(Odds) OR UPDATE(Bookmaker) OR UPDATE(EvaluationSequence)
       OR UPDATE(EvaluationVersion) OR UPDATE(AsOfUtc) OR UPDATE(CreatedAtUtc)
       OR UPDATE(SupersedesEvaluationId) OR UPDATE(BaseModelVersion)
       OR UPDATE(ModelTrainedThroughUtc) OR UPDATE(SelectorVersion)
       OR UPDATE(CalibrationVersion) OR UPDATE(IntelligenceVersion)
       OR UPDATE(SettlementVersion) OR UPDATE(RobustnessVersion) OR UPDATE(PolicyVersion)
       OR UPDATE(EvaluationPayloadJson) OR UPDATE(InputPayloadJson)
       OR UPDATE(HistogramJson) OR UPDATE(RejectionReasonCodesJson)
       OR UPDATE(WarningCodesJson) OR UPDATE(EvaluationMode)
       OR UPDATE(CurrentSystemDecision) OR UPDATE(RobustDecision)
       OR UPDATE(OriginalStake) OR UPDATE(RecommendedStake) OR UPDATE(StakeMultiplier)
       OR UPDATE(RobustnessScore) OR UPDATE(HumanReadableReason)
       OR UPDATE(DirectPrediction) OR UPDATE(HomePrediction) OR UPDATE(AwayPrediction)
       OR UPDATE(ComponentsPrediction) OR UPDATE(ContextPrediction)
       OR UPDATE(ReconciledPrediction) OR UPDATE(ConsensusMinimum)
       OR UPDATE(ConsensusMaximum) OR UPDATE(ConsensusRange) OR UPDATE(CoherenceGap)
       OR UPDATE(DirectDistance) OR UPDATE(ComponentsDistance) OR UPDATE(ContextDistance)
       OR UPDATE(ReconciledDistance) OR UPDATE(WorstCasePrediction)
       OR UPDATE(WorstCaseDistance) OR UPDATE(ErrorScale)
       OR UPDATE(NormalizedDirectDistance) OR UPDATE(NormalizedWorstCaseDistance)
       OR UPDATE(NormalizedConsensusRange) OR UPDATE(NormalizedCoherenceGap)
       OR UPDATE(SideAgreement) OR UPDATE(MagnitudeAgreementScore)
       OR UPDATE(ProbabilityAgreementScore) OR UPDATE(CoherenceScore)
       OR UPDATE(ScenarioSideStability) OR UPDATE(PositiveEvStability)
       OR UPDATE(P01) OR UPDATE(P05) OR UPDATE(P10) OR UPDATE(P25) OR UPDATE(P50)
       OR UPDATE(P75) OR UPDATE(P90) OR UPDATE(P95) OR UPDATE(P99)
       OR UPDATE(DistributionMean) OR UPDATE(StandardDeviation)
       OR UPDATE(MedianAbsoluteDeviation) OR UPDATE(DistributionEffectiveN)
       OR UPDATE(ResidualRawObservationCount) OR UPDATE(SimulationCount)
       OR UPDATE(DistributionMethod) OR UPDATE(DistributionVersion)
       OR UPDATE(PWin) OR UPDATE(PHalfWin) OR UPDATE(PPush) OR UPDATE(PHalfLoss)
       OR UPDATE(PLoss) OR UPDATE(RawProbability) OR UPDATE(CalibratedProbability)
       OR UPDATE(ProbabilityLowerBound) OR UPDATE(ProbabilityUpperBound)
       OR UPDATE(ModelFairOdds) OR UPDATE(ModelFairProbability)
       OR UPDATE(RobustModelFairProbability) OR UPDATE(MarketImpliedProbability)
       OR UPDATE(MarketNoVigProbability) OR UPDATE(ConservativeMarketProbability)
       OR UPDATE(PointEdge) OR UPDATE(RobustEdge) OR UPDATE(PointExpectedValue)
       OR UPDATE(RobustExpectedValue) OR UPDATE(ExpectedValueP10)
       OR UPDATE(ExpectedValueP50) OR UPDATE(ExpectedValueP90)
       OR UPDATE(EdgeP10) OR UPDATE(EdgeP50) OR UPDATE(EdgeP90)
       OR UPDATE(CalibrationEffectiveN) OR UPDATE(CalibrationExactMarketN)
       OR UPDATE(CalibrationFamilyN) OR UPDATE(CalibrationGlobalN)
       OR UPDATE(CalibrationReliability) OR UPDATE(CalibrationSpecificityScore)
       OR UPDATE(CalibrationRecencyScore) OR UPDATE(CalibrationErrorScore)
       OR UPDATE(CalibrationFallbackLevel) OR UPDATE(OddsEvaluated)
       OR UPDATE(OddsTaken) OR UPDATE(OpeningOdds) OR UPDATE(ClosingOdds)
       OR UPDATE(BestAvailableOdds) OR UPDATE(MedianMarketOdds)
       OR UPDATE(QuoteTimestampUtc) OR UPDATE(OddsAgeSeconds)
       OR UPDATE(MinutesToKickoff) OR UPDATE(NoVigMethod) OR UPDATE(OddsReliability)
       OR UPDATE(OpeningLine) OR UPDATE(ClosingLine) OR UPDATE(ClvOdds) OR UPDATE(ClvLine)
       OR UPDATE(LineupStatus) OR UPDATE(IntelligenceEvidenceStatus)
       OR UPDATE(FatigueDataStatus) OR UPDATE(GameStateModelStatus)
       OR UPDATE(ScenarioCount) OR UPDATE(AdverseScenarioProbability)
       OR UPDATE(ScenarioStability)
    BEGIN
        THROW 52206, 'Robust evaluation snapshot evidence is immutable.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS currentRow
        INNER JOIN deleted AS priorRow
          ON priorRow.RobustEvaluationId = currentRow.RobustEvaluationId
        WHERE priorRow.IsCurrent <> 1 OR currentRow.IsCurrent <> 0
    )
        THROW 52207, 'Only an IsCurrent 1 to 0 supersession transition is allowed.', 1;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_AppendAutomatedBotPickRobustEvaluation
    @LogicalPickKey CHAR(64),
    @IdempotencyHash CHAR(64),
    @InputHash CHAR(64),
    @SnapshotHash CHAR(64),
    @SnapshotJson NVARCHAR(MAX),
    @ComponentsJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF LEN(@LogicalPickKey) <> 64 OR LEN(@IdempotencyHash) <> 64
       OR LEN(@InputHash) <> 64 OR LEN(@SnapshotHash) <> 64
        THROW 52208, 'Robust hashes must be lowercase SHA-256 hex values.', 1;
    IF ISJSON(@SnapshotJson) <> 1 OR ISJSON(@ComponentsJson) <> 1
        THROW 52209, 'Robust snapshot and components must be valid JSON.', 1;
    IF LEFT(LTRIM(@SnapshotJson), 1) <> N'{' OR LEFT(LTRIM(@ComponentsJson), 1) <> N'['
        THROW 52214, 'Robust snapshot must be an object and components must be an array.', 1;

    DECLARE @ExistingId BIGINT;
    DECLARE @ExistingSequence INT;
    DECLARE @ExistingSupersedes BIGINT;
    DECLARE @ExistingSnapshotHash CHAR(64);
    DECLARE @PreviousId BIGINT;
    DECLARE @EvaluationSequence INT;
    DECLARE @RobustEvaluationId BIGINT;
    DECLARE @LockResult INT;
    DECLARE @LockResource NVARCHAR(255) = CONCAT(N'RobustPickEvaluation:', @LogicalPickKey);

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 15000;
        IF @LockResult < 0
            THROW 52210, 'Could not acquire the robust-evaluation append lock.', 1;

        SELECT
            @ExistingId = RobustEvaluationId,
            @ExistingSequence = EvaluationSequence,
            @ExistingSupersedes = SupersedesEvaluationId,
            @ExistingSnapshotHash = SnapshotHash
        FROM dbo.AutomatedBotPickRobustEvaluations WITH (UPDLOCK, HOLDLOCK)
        WHERE IdempotencyHash = @IdempotencyHash;

        IF @ExistingId IS NOT NULL
        BEGIN
            IF @ExistingSnapshotHash <> @SnapshotHash
                THROW 52211, 'Determinism violation: identical inputs produced a different robust snapshot.', 1;

            COMMIT TRANSACTION;
            SELECT @ExistingId AS RobustEvaluationId,
                   @ExistingSequence AS EvaluationSequence,
                   CONVERT(BIT, 0) AS Inserted,
                   @ExistingSupersedes AS SupersedesEvaluationId;
            RETURN;
        END;

        SELECT TOP (1) @PreviousId = RobustEvaluationId
        FROM dbo.AutomatedBotPickRobustEvaluations WITH (UPDLOCK, HOLDLOCK)
        WHERE LogicalPickKey = @LogicalPickKey AND IsCurrent = 1
        ORDER BY EvaluationSequence DESC;

        SELECT @EvaluationSequence = ISNULL(MAX(EvaluationSequence), 0) + 1
        FROM dbo.AutomatedBotPickRobustEvaluations WITH (UPDLOCK, HOLDLOCK)
        WHERE LogicalPickKey = @LogicalPickKey;

        IF @PreviousId IS NOT NULL
        BEGIN
            UPDATE dbo.AutomatedBotPickRobustEvaluations
            SET IsCurrent = 0
            WHERE RobustEvaluationId = @PreviousId AND IsCurrent = 1;
        END;

        INSERT dbo.AutomatedBotPickRobustEvaluations
        (
            LogicalPickKey, IdempotencyHash, InputHash, SnapshotHash,
            SourceEvaluationId, BotPickSelectionId, SourceOddsSnapshotId, FixtureId,
            BotKey, MarketFamily, MarketType, Side, Line, Odds, Bookmaker,
            EvaluationSequence, EvaluationVersion, AsOfUtc, IsCurrent, SupersedesEvaluationId,
            BaseModelVersion, ModelTrainedThroughUtc, SelectorVersion, CalibrationVersion,
            IntelligenceVersion, SettlementVersion, RobustnessVersion, PolicyVersion,
            DirectPrediction, HomePrediction, AwayPrediction, ComponentsPrediction,
            ContextPrediction, ReconciledPrediction, ConsensusMinimum, ConsensusMaximum,
            ConsensusRange, CoherenceGap, DirectDistance, ComponentsDistance,
            ContextDistance, ReconciledDistance, WorstCasePrediction, WorstCaseDistance,
            ErrorScale, NormalizedDirectDistance, NormalizedWorstCaseDistance,
            NormalizedConsensusRange, NormalizedCoherenceGap, SideAgreement,
            MagnitudeAgreementScore, ProbabilityAgreementScore, CoherenceScore,
            ScenarioSideStability, PositiveEvStability,
            P01, P05, P10, P25, P50, P75, P90, P95, P99,
            DistributionMean, StandardDeviation, MedianAbsoluteDeviation,
            DistributionEffectiveN, ResidualRawObservationCount, SimulationCount,
            DistributionMethod, DistributionVersion, HistogramJson,
            PWin, PHalfWin, PPush, PHalfLoss, PLoss,
            RawProbability, CalibratedProbability, ProbabilityLowerBound,
            ProbabilityUpperBound, ModelFairOdds, ModelFairProbability,
            RobustModelFairProbability, MarketImpliedProbability,
            MarketNoVigProbability, ConservativeMarketProbability,
            PointEdge, RobustEdge, PointExpectedValue, RobustExpectedValue,
            ExpectedValueP10, ExpectedValueP50, ExpectedValueP90,
            EdgeP10, EdgeP50, EdgeP90,
            CalibrationEffectiveN, CalibrationExactMarketN, CalibrationFamilyN,
            CalibrationGlobalN, CalibrationReliability, CalibrationSpecificityScore,
            CalibrationRecencyScore, CalibrationErrorScore, CalibrationFallbackLevel,
            OddsEvaluated, OddsTaken, OpeningOdds, ClosingOdds, BestAvailableOdds,
            MedianMarketOdds, QuoteTimestampUtc, OddsAgeSeconds, MinutesToKickoff,
            NoVigMethod, OddsReliability, OpeningLine, ClosingLine, ClvOdds, ClvLine,
            LineupStatus, IntelligenceEvidenceStatus, FatigueDataStatus,
            GameStateModelStatus, ScenarioCount, AdverseScenarioProbability, ScenarioStability,
            EvaluationMode, CurrentSystemDecision, RobustDecision, OriginalStake,
            RecommendedStake, StakeMultiplier, RobustnessScore,
            RejectionReasonCodesJson, WarningCodesJson, HumanReadableReason,
            InputPayloadJson, EvaluationPayloadJson
        )
        SELECT
            @LogicalPickKey, @IdempotencyHash, @InputHash, @SnapshotHash,
            snapshot.SourceEvaluationId, snapshot.BotPickSelectionId,
            snapshot.SourceOddsSnapshotId, snapshot.FixtureId,
            snapshot.BotKey, snapshot.MarketFamily, snapshot.MarketType, snapshot.Side,
            snapshot.Line, snapshot.Odds, snapshot.Bookmaker,
            @EvaluationSequence, snapshot.EvaluationVersion, snapshot.AsOfUtc,
            CONVERT(BIT, 1), @PreviousId,
            snapshot.BaseModelVersion, snapshot.ModelTrainedThroughUtc,
            snapshot.SelectorVersion, snapshot.CalibrationVersion,
            snapshot.IntelligenceVersion, snapshot.SettlementVersion,
            snapshot.RobustnessVersion, snapshot.PolicyVersion,
            snapshot.DirectPrediction, snapshot.HomePrediction, snapshot.AwayPrediction,
            snapshot.ComponentsPrediction, snapshot.ContextPrediction,
            snapshot.ReconciledPrediction, snapshot.ConsensusMinimum,
            snapshot.ConsensusMaximum, snapshot.ConsensusRange, snapshot.CoherenceGap,
            snapshot.DirectDistance, snapshot.ComponentsDistance, snapshot.ContextDistance,
            snapshot.ReconciledDistance, snapshot.WorstCasePrediction,
            snapshot.WorstCaseDistance, snapshot.ErrorScale,
            snapshot.NormalizedDirectDistance, snapshot.NormalizedWorstCaseDistance,
            snapshot.NormalizedConsensusRange, snapshot.NormalizedCoherenceGap,
            snapshot.SideAgreement, snapshot.MagnitudeAgreementScore,
            snapshot.ProbabilityAgreementScore, snapshot.CoherenceScore,
            snapshot.ScenarioSideStability, snapshot.PositiveEvStability,
            snapshot.P01, snapshot.P05, snapshot.P10, snapshot.P25, snapshot.P50,
            snapshot.P75, snapshot.P90, snapshot.P95, snapshot.P99,
            snapshot.DistributionMean, snapshot.StandardDeviation,
            snapshot.MedianAbsoluteDeviation, snapshot.DistributionEffectiveN,
            snapshot.ResidualRawObservationCount, snapshot.SimulationCount,
            snapshot.DistributionMethod, snapshot.DistributionVersion,
            snapshot.HistogramJson, snapshot.PWin, snapshot.PHalfWin,
            snapshot.PPush, snapshot.PHalfLoss, snapshot.PLoss,
            snapshot.RawProbability, snapshot.CalibratedProbability,
            snapshot.ProbabilityLowerBound, snapshot.ProbabilityUpperBound,
            snapshot.ModelFairOdds, snapshot.ModelFairProbability,
            snapshot.RobustModelFairProbability, snapshot.MarketImpliedProbability,
            snapshot.MarketNoVigProbability, snapshot.ConservativeMarketProbability,
            snapshot.PointEdge, snapshot.RobustEdge, snapshot.PointExpectedValue,
            snapshot.RobustExpectedValue, snapshot.ExpectedValueP10,
            snapshot.ExpectedValueP50, snapshot.ExpectedValueP90,
            snapshot.EdgeP10, snapshot.EdgeP50, snapshot.EdgeP90,
            snapshot.CalibrationEffectiveN, snapshot.CalibrationExactMarketN,
            snapshot.CalibrationFamilyN, snapshot.CalibrationGlobalN,
            snapshot.CalibrationReliability, snapshot.CalibrationSpecificityScore,
            snapshot.CalibrationRecencyScore, snapshot.CalibrationErrorScore,
            snapshot.CalibrationFallbackLevel, snapshot.OddsEvaluated, snapshot.OddsTaken,
            snapshot.OpeningOdds, snapshot.ClosingOdds, snapshot.BestAvailableOdds,
            snapshot.MedianMarketOdds, snapshot.QuoteTimestampUtc, snapshot.OddsAgeSeconds,
            snapshot.MinutesToKickoff, snapshot.NoVigMethod, snapshot.OddsReliability,
            snapshot.OpeningLine, snapshot.ClosingLine, snapshot.ClvOdds, snapshot.ClvLine,
            snapshot.LineupStatus, snapshot.IntelligenceEvidenceStatus,
            snapshot.FatigueDataStatus, snapshot.GameStateModelStatus,
            snapshot.ScenarioCount, snapshot.AdverseScenarioProbability,
            snapshot.ScenarioStability, snapshot.EvaluationMode,
            snapshot.CurrentSystemDecision, snapshot.RobustDecision,
            snapshot.OriginalStake, snapshot.RecommendedStake,
            snapshot.StakeMultiplier, snapshot.RobustnessScore,
            snapshot.RejectionReasonCodesJson, snapshot.WarningCodesJson,
            snapshot.HumanReadableReason, snapshot.InputPayloadJson,
            snapshot.EvaluationPayloadJson
        FROM OPENJSON(@SnapshotJson)
        WITH
        (
            SourceEvaluationId BIGINT '$.sourceEvaluationId',
            BotPickSelectionId BIGINT '$.botPickSelectionId',
            SourceOddsSnapshotId BIGINT '$.sourceOddsSnapshotId',
            FixtureId BIGINT '$.fixtureId',
            BotKey NVARCHAR(50) '$.botKey', MarketFamily NVARCHAR(30) '$.marketFamily',
            MarketType NVARCHAR(50) '$.marketType', Side NVARCHAR(10) '$.side',
            Line DECIMAL(10,2) '$.line', Odds DECIMAL(18,6) '$.odds',
            Bookmaker NVARCHAR(50) '$.bookmaker', EvaluationVersion NVARCHAR(80) '$.evaluationVersion',
            AsOfUtc DATETIME2(3) '$.asOfUtc', BaseModelVersion NVARCHAR(120) '$.baseModelVersion',
            ModelTrainedThroughUtc DATETIME2(3) '$.modelTrainedThroughUtc',
            SelectorVersion NVARCHAR(120) '$.selectorVersion', CalibrationVersion NVARCHAR(120) '$.calibrationVersion',
            IntelligenceVersion NVARCHAR(120) '$.intelligenceVersion', SettlementVersion NVARCHAR(120) '$.settlementVersion',
            RobustnessVersion NVARCHAR(120) '$.robustnessVersion', PolicyVersion NVARCHAR(120) '$.policyVersion',
            DirectPrediction DECIMAL(18,6) '$.directPrediction', HomePrediction DECIMAL(18,6) '$.homePrediction',
            AwayPrediction DECIMAL(18,6) '$.awayPrediction', ComponentsPrediction DECIMAL(18,6) '$.componentsPrediction',
            ContextPrediction DECIMAL(18,6) '$.contextPrediction', ReconciledPrediction DECIMAL(18,6) '$.reconciledPrediction',
            ConsensusMinimum DECIMAL(18,6) '$.consensusMinimum', ConsensusMaximum DECIMAL(18,6) '$.consensusMaximum',
            ConsensusRange DECIMAL(18,6) '$.consensusRange', CoherenceGap DECIMAL(18,6) '$.coherenceGap',
            DirectDistance DECIMAL(18,6) '$.directDistance', ComponentsDistance DECIMAL(18,6) '$.componentsDistance',
            ContextDistance DECIMAL(18,6) '$.contextDistance', ReconciledDistance DECIMAL(18,6) '$.reconciledDistance',
            WorstCasePrediction DECIMAL(18,6) '$.worstCasePrediction', WorstCaseDistance DECIMAL(18,6) '$.worstCaseDistance',
            ErrorScale DECIMAL(18,6) '$.errorScale', NormalizedDirectDistance DECIMAL(18,6) '$.normalizedDirectDistance',
            NormalizedWorstCaseDistance DECIMAL(18,6) '$.normalizedWorstCaseDistance',
            NormalizedConsensusRange DECIMAL(18,6) '$.normalizedConsensusRange',
            NormalizedCoherenceGap DECIMAL(18,6) '$.normalizedCoherenceGap', SideAgreement BIT '$.sideAgreement',
            MagnitudeAgreementScore DECIMAL(9,6) '$.magnitudeAgreementScore',
            ProbabilityAgreementScore DECIMAL(9,6) '$.probabilityAgreementScore', CoherenceScore DECIMAL(9,6) '$.coherenceScore',
            ScenarioSideStability DECIMAL(9,6) '$.scenarioSideStability', PositiveEvStability DECIMAL(9,6) '$.positiveEvStability',
            P01 DECIMAL(18,6) '$.p01', P05 DECIMAL(18,6) '$.p05', P10 DECIMAL(18,6) '$.p10',
            P25 DECIMAL(18,6) '$.p25', P50 DECIMAL(18,6) '$.p50', P75 DECIMAL(18,6) '$.p75',
            P90 DECIMAL(18,6) '$.p90', P95 DECIMAL(18,6) '$.p95', P99 DECIMAL(18,6) '$.p99',
            DistributionMean DECIMAL(18,6) '$.distributionMean', StandardDeviation DECIMAL(18,6) '$.standardDeviation',
            MedianAbsoluteDeviation DECIMAL(18,6) '$.medianAbsoluteDeviation',
            DistributionEffectiveN DECIMAL(18,6) '$.distributionEffectiveN',
            ResidualRawObservationCount INT '$.residualRawObservationCount', SimulationCount INT '$.simulationCount',
            DistributionMethod NVARCHAR(80) '$.distributionMethod', DistributionVersion NVARCHAR(120) '$.distributionVersion',
            HistogramJson NVARCHAR(MAX) '$.histogramJson', PWin DECIMAL(9,6) '$.pWin',
            PHalfWin DECIMAL(9,6) '$.pHalfWin', PPush DECIMAL(9,6) '$.pPush',
            PHalfLoss DECIMAL(9,6) '$.pHalfLoss', PLoss DECIMAL(9,6) '$.pLoss',
            RawProbability DECIMAL(9,6) '$.rawProbability', CalibratedProbability DECIMAL(9,6) '$.calibratedProbability',
            ProbabilityLowerBound DECIMAL(9,6) '$.probabilityLowerBound', ProbabilityUpperBound DECIMAL(9,6) '$.probabilityUpperBound',
            ModelFairOdds DECIMAL(18,6) '$.modelFairOdds', ModelFairProbability DECIMAL(9,6) '$.modelFairProbability',
            RobustModelFairProbability DECIMAL(9,6) '$.robustModelFairProbability',
            MarketImpliedProbability DECIMAL(9,6) '$.marketImpliedProbability',
            MarketNoVigProbability DECIMAL(9,6) '$.marketNoVigProbability',
            ConservativeMarketProbability DECIMAL(9,6) '$.conservativeMarketProbability',
            PointEdge DECIMAL(12,6) '$.pointEdge', RobustEdge DECIMAL(12,6) '$.robustEdge',
            PointExpectedValue DECIMAL(12,6) '$.pointExpectedValue', RobustExpectedValue DECIMAL(12,6) '$.robustExpectedValue',
            ExpectedValueP10 DECIMAL(12,6) '$.expectedValueP10', ExpectedValueP50 DECIMAL(12,6) '$.expectedValueP50',
            ExpectedValueP90 DECIMAL(12,6) '$.expectedValueP90', EdgeP10 DECIMAL(12,6) '$.edgeP10',
            EdgeP50 DECIMAL(12,6) '$.edgeP50', EdgeP90 DECIMAL(12,6) '$.edgeP90',
            CalibrationEffectiveN DECIMAL(18,6) '$.calibrationEffectiveN',
            CalibrationExactMarketN INT '$.calibrationExactMarketN', CalibrationFamilyN INT '$.calibrationFamilyN',
            CalibrationGlobalN INT '$.calibrationGlobalN', CalibrationReliability DECIMAL(9,6) '$.calibrationReliability',
            CalibrationSpecificityScore DECIMAL(9,6) '$.calibrationSpecificityScore',
            CalibrationRecencyScore DECIMAL(9,6) '$.calibrationRecencyScore',
            CalibrationErrorScore DECIMAL(9,6) '$.calibrationErrorScore',
            CalibrationFallbackLevel NVARCHAR(80) '$.calibrationFallbackLevel',
            OddsEvaluated DECIMAL(18,6) '$.oddsEvaluated', OddsTaken DECIMAL(18,6) '$.oddsTaken',
            OpeningOdds DECIMAL(18,6) '$.openingOdds', ClosingOdds DECIMAL(18,6) '$.closingOdds',
            BestAvailableOdds DECIMAL(18,6) '$.bestAvailableOdds', MedianMarketOdds DECIMAL(18,6) '$.medianMarketOdds',
            QuoteTimestampUtc DATETIME2(3) '$.quoteTimestampUtc', OddsAgeSeconds INT '$.oddsAgeSeconds',
            MinutesToKickoff INT '$.minutesToKickoff', NoVigMethod NVARCHAR(40) '$.noVigMethod',
            OddsReliability DECIMAL(9,6) '$.oddsReliability', OpeningLine DECIMAL(10,2) '$.openingLine',
            ClosingLine DECIMAL(10,2) '$.closingLine', ClvOdds DECIMAL(12,6) '$.clvOdds', ClvLine DECIMAL(12,6) '$.clvLine',
            LineupStatus NVARCHAR(40) '$.lineupStatus', IntelligenceEvidenceStatus NVARCHAR(40) '$.intelligenceEvidenceStatus',
            FatigueDataStatus NVARCHAR(40) '$.fatigueDataStatus', GameStateModelStatus NVARCHAR(40) '$.gameStateModelStatus',
            ScenarioCount INT '$.scenarioCount', AdverseScenarioProbability DECIMAL(9,6) '$.adverseScenarioProbability',
            ScenarioStability DECIMAL(9,6) '$.scenarioStability', EvaluationMode NVARCHAR(20) '$.evaluationMode',
            CurrentSystemDecision NVARCHAR(30) '$.currentSystemDecision', RobustDecision NVARCHAR(30) '$.robustDecision',
            OriginalStake DECIMAL(12,4) '$.originalStake', RecommendedStake DECIMAL(12,4) '$.recommendedStake',
            StakeMultiplier DECIMAL(9,6) '$.stakeMultiplier', RobustnessScore DECIMAL(9,6) '$.robustnessScore',
            RejectionReasonCodesJson NVARCHAR(MAX) '$.rejectionReasonCodesJson', WarningCodesJson NVARCHAR(MAX) '$.warningCodesJson',
            HumanReadableReason NVARCHAR(2000) '$.humanReadableReason', InputPayloadJson NVARCHAR(MAX) '$.inputPayloadJson',
            EvaluationPayloadJson NVARCHAR(MAX) '$.evaluationPayloadJson'
        ) AS snapshot;

        SET @RobustEvaluationId = SCOPE_IDENTITY();

        INSERT dbo.AutomatedBotPickRobustComponents
        (
            RobustEvaluationId, ComponentSequence, ComponentType, PredictedValue,
            ProbabilityForSelection, Weight, IsUsable, SourceVersion, AsOfUtc,
            ExclusionReason, DataQualityScore, MetadataJson
        )
        SELECT
            @RobustEvaluationId, component.ComponentSequence, component.ComponentType,
            component.PredictedValue, component.ProbabilityForSelection,
            component.Weight, component.IsUsable, component.SourceVersion,
            component.AsOfUtc, component.ExclusionReason,
            component.DataQualityScore, component.MetadataJson
        FROM OPENJSON(@ComponentsJson)
        WITH
        (
            ComponentSequence INT '$.componentSequence',
            ComponentType NVARCHAR(40) '$.componentType',
            PredictedValue DECIMAL(18,6) '$.predictedValue',
            ProbabilityForSelection DECIMAL(9,6) '$.probabilityForSelection',
            Weight DECIMAL(9,6) '$.weight',
            IsUsable BIT '$.isUsable',
            SourceVersion NVARCHAR(120) '$.sourceVersion',
            AsOfUtc DATETIME2(3) '$.asOfUtc',
            ExclusionReason NVARCHAR(500) '$.exclusionReason',
            DataQualityScore DECIMAL(9,6) '$.dataQualityScore',
            MetadataJson NVARCHAR(MAX) '$.metadataJson'
        ) AS component;

        COMMIT TRANSACTION;
        SELECT @RobustEvaluationId AS RobustEvaluationId,
               @EvaluationSequence AS EvaluationSequence,
               CONVERT(BIT, 1) AS Inserted,
               @PreviousId AS SupersedesEvaluationId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.AutomatedBotRobustPolicies
    WHERE PolicyVersion = N'robust-policy-shadow-1.0.0'
      AND BotKey IS NULL AND MarketFamily IS NULL AND MarketType IS NULL
)
BEGIN
    INSERT dbo.AutomatedBotRobustPolicies
    (
        PolicyHash, PolicyVersion, EffectiveFromUtc, EvaluationMode,
        ConfigurationJson, CreatedBy
    )
    VALUES
    (
        LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256',
            N'robust-policy-shadow-1.0.0|GLOBAL|Shadow'), 2)),
        N'robust-policy-shadow-1.0.0',
        CONVERT(DATETIME2(3), N'2026-08-29T00:00:00.000'),
        N'Shadow',
        N'{"minRobustEdge":0.005,"minRobustExpectedValue":0.0,"minPositiveEvStability":0.75,"minScenarioSideStability":0.75,"minNormalizedWorstCaseDistance":0.25,"maxNormalizedConsensusRange":0.75,"maxNormalizedCoherenceGap":0.75,"minCalibrationReliability":0.50,"requireSideAgreement":true}',
        N'migration'
    );
END;

GO
