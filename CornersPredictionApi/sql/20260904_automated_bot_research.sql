SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NULL
    THROW 52200, 'AutomatedBotPickEvaluations must exist before the research delta.', 1;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'EvidenceSnapshotHash') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD EvidenceSnapshotHash CHAR(64) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'ProductionDecision') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ProductionDecision NVARCHAR(30) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'ProductionReason') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ProductionReason NVARCHAR(1000) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'IsResearchWinner') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD IsResearchWinner BIT NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'SelectionScore') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD SelectionScore DECIMAL(9,6) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'OddsTimestampUtc') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD OddsTimestampUtc DATETIME2(3) NULL;

IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PredictionTimestampUtc') IS NULL
    ALTER TABLE dbo.AutomatedBotPickEvaluations ADD PredictionTimestampUtc DATETIME2(3) NULL;

GO

-- G originally installed a narrower constraint. Replace it only when its shape is
-- stale, disabled or untrusted. There are deliberately no historical row updates.
IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'CK_AutomatedBotPickEvaluations_PublicationStatus'
      AND
      (
          is_disabled = 1
          OR is_not_trusted = 1
          OR definition NOT LIKE N'%Shadow%'
          OR definition NOT LIKE N'%Published%'
          OR definition NOT LIKE N'%NotSelected%'
          OR definition NOT LIKE N'%Eligible%'
          OR definition NOT LIKE N'%ProductionBlocked%'
          OR definition NOT LIKE N'%ModelRejected%'
          OR definition NOT LIKE N'%PendingData%'
      )
)
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations
        DROP CONSTRAINT CK_AutomatedBotPickEvaluations_PublicationStatus;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'CK_AutomatedBotPickEvaluations_PublicationStatus'
)
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations WITH CHECK
        ADD CONSTRAINT CK_AutomatedBotPickEvaluations_PublicationStatus CHECK
        (
            PublicationStatus IS NULL OR
            PublicationStatus IN
            (
                N'Shadow', N'Published', N'NotSelected', N'Eligible',
                N'ProductionBlocked', N'ModelRejected', N'PendingData'
            )
        );
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
      AND name = N'IX_AutomatedBotPickEvaluations_ResearchPage'
)
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_ResearchPage
        ON dbo.AutomatedBotPickEvaluations
           (MatchDate DESC, AutomatedBotPickEvaluationId DESC)
        INCLUDE
        (
            BotKey,
            MarketType,
            Decision,
            PublicationStatus,
            Published,
            PublishedSelectionId,
            ProductionDecision,
            IsResearchWinner
        );
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedBotPickEvaluation
    @IdempotencyKey CHAR(64),
    @RunId UNIQUEIDENTIFIER,
    @BotKey NVARCHAR(50),
    @AutomationVersion NVARCHAR(50),
    @PartidoProximoCuotaId BIGINT,
    @ApiFootballFixtureId BIGINT = NULL,
    @MatchDate DATETIME2(0),
    @League NVARCHAR(200),
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @Source NVARCHAR(50),
    @SourceMarketType NVARCHAR(50),
    @MarketType NVARCHAR(50),
    @LineValue DECIMAL(6,2),
    @SelectedSide NVARCHAR(10) = NULL,
    @SelectedOdds DECIMAL(10,2) = NULL,
    @DecisionEngineType NVARCHAR(40),
    @Decision NVARCHAR(20),
    @BaseModelName NVARCHAR(120) = NULL,
    @BaseModelVersion NVARCHAR(120) = NULL,
    @BaseModelTrainedThroughUtc DATETIME2(0) = NULL,
    @FeatureSchemaVersion NVARCHAR(80),
    @ConfigurationVersion NVARCHAR(80),
    @BaseRawProbability DECIMAL(9,6) = NULL,
    @BaseCalibratedProbability DECIMAL(9,6) = NULL,
    @RawImpliedProbability DECIMAL(9,6) = NULL,
    @MarketNoVigProbability DECIMAL(9,6) = NULL,
    @FinalProbability DECIMAL(9,6) = NULL,
    @FinalEdge DECIMAL(9,6) = NULL,
    @FinalExpectedValue DECIMAL(9,6) = NULL,
    @RuleBasedConfidenceScore DECIMAL(9,6) = NULL,
    @ContextExpectedValue DECIMAL(12,4) = NULL,
    @ContextAgreementScore DECIMAL(9,6) = NULL,
    @DataQualityScore DECIMAL(9,6) = NULL,
    @DecisionReasonsJson NVARCHAR(MAX),
    @RiskFlagsJson NVARCHAR(MAX),
    @Explanation NVARCHAR(1000),
    @FeatureSnapshotJson NVARCHAR(MAX),
    @EvidenceSnapshotHash CHAR(64) = NULL,
    @PublishedSelectionId BIGINT = NULL,
    @PublicationStatus NVARCHAR(20) = NULL,
    @ProductionDecision NVARCHAR(30) = NULL,
    @ProductionReason NVARCHAR(1000) = NULL,
    @IsResearchWinner BIT = 0,
    @SelectionScore DECIMAL(9,6) = NULL,
    @OddsTimestampUtc DATETIME2(3) = NULL,
    @PredictionTimestampUtc DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Scientific inputs are immutable for an idempotency key. A retry may only
    -- enrich fixture/publication linkage; a changed snapshot receives a new key.
    MERGE dbo.AutomatedBotPickEvaluations WITH (HOLDLOCK) AS target
    USING (SELECT @IdempotencyKey AS IdempotencyKey) AS source
       ON target.IdempotencyKey = source.IdempotencyKey
    WHEN MATCHED THEN UPDATE SET
        ApiFootballFixtureId = COALESCE(target.ApiFootballFixtureId, @ApiFootballFixtureId),
        Published = CASE
            WHEN target.PublishedSelectionId IS NOT NULL OR @PublishedSelectionId IS NOT NULL THEN 1
            ELSE COALESCE(target.Published, 0)
        END,
        PublicationStatus = CASE
            WHEN target.PublishedSelectionId IS NOT NULL OR @PublishedSelectionId IS NOT NULL THEN N'Published'
            ELSE COALESCE(@PublicationStatus, target.PublicationStatus)
        END,
        ProductionDecision = CASE
            WHEN target.PublishedSelectionId IS NOT NULL OR @PublishedSelectionId IS NOT NULL THEN N'Published'
            ELSE COALESCE(@ProductionDecision, target.ProductionDecision)
        END,
        ProductionReason = CASE
            WHEN target.PublishedSelectionId IS NOT NULL AND @PublishedSelectionId IS NULL THEN target.ProductionReason
            ELSE COALESCE(@ProductionReason, target.ProductionReason)
        END,
        PublishedSelectionId = COALESCE(target.PublishedSelectionId, @PublishedSelectionId),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT
    (
        IdempotencyKey, RunId, BotKey, AutomationVersion, PartidoProximoCuotaId,
        ApiFootballFixtureId, MatchDate, League, HomeTeam, AwayTeam, Source,
        SourceMarketType, MarketType, LineValue, SelectedSide, SelectedOdds,
        DecisionEngineType, Decision, BaseModelName, BaseModelVersion,
        BaseModelTrainedThroughUtc,
        FeatureSchemaVersion, ConfigurationVersion, BaseRawProbability,
        BaseCalibratedProbability, RawImpliedProbability, MarketNoVigProbability,
        FinalProbability, FinalEdge, FinalExpectedValue, RuleBasedConfidenceScore,
        ContextExpectedValue, ContextAgreementScore, DataQualityScore,
        DecisionReasonsJson, RiskFlagsJson, Explanation, FeatureSnapshotJson,
        EvidenceSnapshotHash, Published, PublicationStatus, ProductionDecision,
        ProductionReason, IsResearchWinner, SelectionScore, OddsTimestampUtc,
        PredictionTimestampUtc, PublishedSelectionId
    )
    VALUES
    (
        @IdempotencyKey, @RunId, @BotKey, @AutomationVersion, @PartidoProximoCuotaId,
        @ApiFootballFixtureId, @MatchDate, @League, @HomeTeam, @AwayTeam, @Source,
        @SourceMarketType, @MarketType, @LineValue, @SelectedSide, @SelectedOdds,
        @DecisionEngineType, @Decision, @BaseModelName, @BaseModelVersion,
        @BaseModelTrainedThroughUtc,
        @FeatureSchemaVersion, @ConfigurationVersion, @BaseRawProbability,
        @BaseCalibratedProbability, @RawImpliedProbability, @MarketNoVigProbability,
        @FinalProbability, @FinalEdge, @FinalExpectedValue, @RuleBasedConfidenceScore,
        @ContextExpectedValue, @ContextAgreementScore, @DataQualityScore,
        @DecisionReasonsJson, @RiskFlagsJson, @Explanation, @FeatureSnapshotJson,
        @EvidenceSnapshotHash,
        CASE WHEN @PublishedSelectionId IS NULL THEN 0 ELSE 1 END,
        COALESCE(
            @PublicationStatus,
            CASE
                WHEN @PublishedSelectionId IS NOT NULL THEN N'Published'
                WHEN @Decision = N'Approved' THEN N'Shadow'
                WHEN @Decision = N'PendingData' THEN N'PendingData'
                ELSE N'ModelRejected'
            END),
        COALESCE(
            @ProductionDecision,
            CASE
                WHEN @PublishedSelectionId IS NOT NULL THEN N'Published'
                WHEN @Decision = N'Approved' THEN N'Shadow'
                WHEN @Decision = N'PendingData' THEN N'NotEvaluated'
                ELSE N'ModelRejected'
            END),
        COALESCE(@ProductionReason, @Explanation),
        @IsResearchWinner,
        @SelectionScore,
        @OddsTimestampUtc,
        @PredictionTimestampUtc,
        @PublishedSelectionId
    );
END;

GO
