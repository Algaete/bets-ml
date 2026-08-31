/*
    Bot H2026 shadow laboratory.

    Additive and idempotent.  The laboratory is append-only, captures the exact
    immutable odds snapshot used by an H decision, and derives settlement at query
    time from official MatchHistory evidence that became available after the
    decision.  It never inserts or updates AutomatedCornerBetSelections.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

GO

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NULL
    THROW 52100, 'Bot H shadow lab requires AutomatedBotDefinitions.', 1;
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NULL
    THROW 52101, 'Bot H shadow lab requires AutomatedBotPickEvaluations.', 1;
IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NULL
    THROW 52102, 'Bot H shadow lab requires AutomatedCornerBetSelections.', 1;
IF OBJECT_ID(N'dbo.CornerOddsSnapshots', N'U') IS NULL
    THROW 52103, 'Bot H shadow lab requires CornerOddsSnapshots.', 1;
IF OBJECT_ID(N'dbo.MatchHistory', N'U') IS NULL
    THROW 52104, 'Bot H shadow lab requires MatchHistory.', 1;
IF OBJECT_ID(N'dbo.PartidosProximosCuotas', N'U') IS NULL
    THROW 52127, 'Bot H shadow lab requires PartidosProximosCuotas lineage.', 1;
IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', N'PredictionTimestampUtc') IS NULL
    THROW 52105, 'Apply the Bot G audit-column migration before the Bot H shadow lab.', 1;
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballFixtureId') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'FixtureStatus') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballCornersAvailable') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballUpdatedAtUtc') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'StandardizedHomeTeam') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'StandardizedAwayTeam') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'HomeCorners') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'AwayCorners') IS NULL
    THROW 52129, 'Apply MatchHistory API-Football/canonical lineage before the Bot H shadow lab.', 1;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.AutomatedBotDefinitions
    WHERE BotKey = N'H2026'
)
    THROW 52106, 'Bot H2026 must be seeded before installing its shadow lab.', 1;

-- H is permanently shadow-only.  Migration is allowed to repair only this gate;
-- no strategy settings, history or other bot definitions are changed.
UPDATE dbo.AutomatedBotDefinitions
SET PublishEnabled = 0,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE BotKey = N'H2026'
  AND PublishEnabled <> 0;

IF EXISTS
(
    SELECT 1
    FROM dbo.AutomatedCornerBetSelections
    WHERE BotKey = N'H2026'
)
    THROW 52107, 'Existing H2026 published selections require manual audit before migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.AutomatedBotPickEvaluations
    WHERE BotKey = N'H2026'
      AND
      (
          PublishedSelectionId IS NOT NULL
          OR ISNULL(Published, 0) = 1
          OR PublicationStatus = N'Published'
      )
)
    THROW 52108, 'Existing H2026 publication links require manual audit before migration.', 1;

GO

IF OBJECT_ID(N'dbo.BotH2026ShadowEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BotH2026ShadowEvaluations
    (
        ShadowEvaluationId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BotH2026ShadowEvaluations PRIMARY KEY,
        CaptureKey CHAR(64) NOT NULL,
        SourceEvaluationId BIGINT NOT NULL,
        RunId UNIQUEIDENTIFIER NOT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        AutomationVersion NVARCHAR(50) NOT NULL,
        PartidoProximoCuotaId BIGINT NOT NULL,
        OddsSnapshotId BIGINT NOT NULL,
        OddsCapturedAtUtc DATETIME2(3) NOT NULL,
        PredictionTimestampUtc DATETIME2(3) NOT NULL,
        FixtureDateUtc DATETIME2(3) NOT NULL,
        ApiFootballFixtureId BIGINT NULL,
        Source NVARCHAR(50) NOT NULL,
        SourceMatchId NVARCHAR(100) NULL,
        SourceMatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(200) NOT NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        SourceMarketType NVARCHAR(50) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        LineValue DECIMAL(6,2) NOT NULL,
        Selection NVARCHAR(10) NOT NULL,
        OverOdds DECIMAL(18,6) NULL,
        UnderOdds DECIMAL(18,6) NULL,
        SelectedOdds DECIMAL(18,6) NOT NULL,
        Decision NVARCHAR(20) NOT NULL,
        DecisionEngineType NVARCHAR(40) NOT NULL,
        ConfigurationVersion NVARCHAR(80) NOT NULL,
        FeatureSchemaVersion NVARCHAR(80) NOT NULL,
        BaseModelName NVARCHAR(120) NULL,
        BaseModelVersion NVARCHAR(120) NULL,
        BaseModelTrainedThroughUtc DATETIME2(0) NULL,
        BaseRawProbability DECIMAL(9,6) NULL,
        BaseCalibratedProbability DECIMAL(9,6) NULL,
        RawImpliedProbability DECIMAL(9,6) NULL,
        MarketNoVigProbability DECIMAL(9,6) NULL,
        FinalProbability DECIMAL(9,6) NULL,
        FinalEdge DECIMAL(9,6) NULL,
        FinalExpectedValue DECIMAL(9,6) NULL,
        SelectionScore DECIMAL(9,6) NULL,
        ContextAgreementScore DECIMAL(9,6) NULL,
        DataQualityScore DECIMAL(9,6) NULL,
        VirtualStakeUnits DECIMAL(9,4) NOT NULL,
        DecisionReasonsJson NVARCHAR(MAX) NOT NULL,
        RiskFlagsJson NVARCHAR(MAX) NOT NULL,
        Explanation NVARCHAR(1000) NOT NULL,
        FeatureSnapshotJson NVARCHAR(MAX) NOT NULL,
        FeatureSnapshotHash BINARY(32) NOT NULL,
        CapturedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_BotH2026ShadowEvaluations_CapturedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BotH2026ShadowEvaluations_CaptureKey UNIQUE (CaptureKey),
        CONSTRAINT CK_BotH2026ShadowEvaluations_BotKey CHECK (BotKey = N'H2026'),
        CONSTRAINT CK_BotH2026ShadowEvaluations_Market CHECK
            (MarketType IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')),
        CONSTRAINT CK_BotH2026ShadowEvaluations_Selection CHECK
            (Selection IN (N'Over', N'Under')),
        CONSTRAINT CK_BotH2026ShadowEvaluations_Decision CHECK
            (Decision IN (N'Approved', N'Rejected')),
        CONSTRAINT CK_BotH2026ShadowEvaluations_AsianLine CHECK
            (LineValue >= 0 AND LineValue * 4 = FLOOR(LineValue * 4)),
        CONSTRAINT CK_BotH2026ShadowEvaluations_SelectedOdds CHECK (SelectedOdds > 1),
        CONSTRAINT CK_BotH2026ShadowEvaluations_VirtualStake CHECK
            (VirtualStakeUnits > 0 AND VirtualStakeUnits <= 10),
        CONSTRAINT CK_BotH2026ShadowEvaluations_Temporal CHECK
            (OddsCapturedAtUtc <= PredictionTimestampUtc AND PredictionTimestampUtc < FixtureDateUtc),
        CONSTRAINT CK_BotH2026ShadowEvaluations_FeatureJson CHECK (ISJSON(FeatureSnapshotJson) = 1),
        CONSTRAINT CK_BotH2026ShadowEvaluations_DecisionReasonsJson CHECK (ISJSON(DecisionReasonsJson) = 1),
        CONSTRAINT CK_BotH2026ShadowEvaluations_RiskFlagsJson CHECK (ISJSON(RiskFlagsJson) = 1)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotH2026ShadowEvaluations')
      AND name = N'IX_BotH2026ShadowEvaluations_SourceEvaluation'
)
BEGIN
    -- Legacy data can contain more than one immutable capture for a source audit.
    -- A non-unique seek index preserves those records while turning restart
    -- backfill from a 49k-row cursor into a scan of genuinely missing rows only.
    CREATE INDEX IX_BotH2026ShadowEvaluations_SourceEvaluation
        ON dbo.BotH2026ShadowEvaluations(SourceEvaluationId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotH2026ShadowEvaluations')
      AND name = N'IX_BotH2026ShadowEvaluations_ScorecardWindow'
)
BEGIN
    -- Scorecards never use more than 90 fixture-days.  Lead with FixtureDateUtc
    -- so SQL Server does not reconcile the complete append-only laboratory.
    CREATE INDEX IX_BotH2026ShadowEvaluations_ScorecardWindow
        ON dbo.BotH2026ShadowEvaluations(FixtureDateUtc DESC, ShadowEvaluationId DESC)
        INCLUDE
        (
            PredictionTimestampUtc, ConfigurationVersion, MarketType, Selection,
            Decision, ApiFootballFixtureId, Source, SourceMatchId, SourceMatchDate,
            HomeTeam, AwayTeam, SelectedOdds, VirtualStakeUnits,
            FinalProbability, MarketNoVigProbability, FinalEdge, FinalExpectedValue
        );
END;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotH2026ShadowEvaluations')
      AND name = N'IX_BotH2026ShadowEvaluations_Prediction'
)
BEGIN
    CREATE INDEX IX_BotH2026ShadowEvaluations_Prediction
        ON dbo.BotH2026ShadowEvaluations(PredictionTimestampUtc DESC, ShadowEvaluationId DESC)
        INCLUDE
        (
            ConfigurationVersion, MarketType, Selection, Decision,
            ApiFootballFixtureId, OddsSnapshotId, FinalProbability,
            MarketNoVigProbability, SelectedOdds, VirtualStakeUnits
        );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotH2026ShadowEvaluations')
      AND name = N'IX_BotH2026ShadowEvaluations_Fixture'
)
BEGIN
    CREATE INDEX IX_BotH2026ShadowEvaluations_Fixture
        ON dbo.BotH2026ShadowEvaluations(ApiFootballFixtureId, SourceMatchDate)
        INCLUDE
        (
            HomeTeam, AwayTeam, PredictionTimestampUtc, MarketType,
            Selection, LineValue, SelectedOdds, Decision
        );
END;

GO

/*
    Internal capture procedure.  Strict mode is used by the live trigger and rolls
    back the source audit upsert if temporal or immutable quote lineage is missing.
    Non-strict mode is used once for legacy backfill and simply omits unverifiable
    rows; status reports those rows explicitly.
*/
CREATE OR ALTER PROCEDURE dbo.sp_CaptureBotH2026ShadowEvaluation
    @SourceEvaluationId BIGINT,
    @Strict BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
        @BotKey NVARCHAR(50),
        @RunId UNIQUEIDENTIFIER,
        @AutomationVersion NVARCHAR(50),
        @PartidoProximoCuotaId BIGINT,
        @ApiFootballFixtureId BIGINT,
        @Source NVARCHAR(50),
        @SourceMatchDate DATETIME2(0),
        @League NVARCHAR(200),
        @HomeTeam NVARCHAR(150),
        @AwayTeam NVARCHAR(150),
        @SourceMarketType NVARCHAR(50),
        @MarketType NVARCHAR(50),
        @LineValue DECIMAL(6,2),
        @Selection NVARCHAR(10),
        @EvaluationSelectedOdds DECIMAL(18,6),
        @Decision NVARCHAR(20),
        @DecisionEngineType NVARCHAR(40),
        @ConfigurationVersion NVARCHAR(80),
        @FeatureSchemaVersion NVARCHAR(80),
        @BaseModelName NVARCHAR(120),
        @BaseModelVersion NVARCHAR(120),
        @BaseModelTrainedThroughUtc DATETIME2(0),
        @BaseRawProbability DECIMAL(9,6),
        @BaseCalibratedProbability DECIMAL(9,6),
        @RawImpliedProbability DECIMAL(9,6),
        @MarketNoVigProbability DECIMAL(9,6),
        @FinalProbability DECIMAL(9,6),
        @FinalEdge DECIMAL(9,6),
        @FinalExpectedValue DECIMAL(9,6),
        @SelectionScore DECIMAL(9,6),
        @ContextAgreementScore DECIMAL(9,6),
        @DataQualityScore DECIMAL(9,6),
        @DecisionReasonsJson NVARCHAR(MAX),
        @RiskFlagsJson NVARCHAR(MAX),
        @Explanation NVARCHAR(1000),
        @FeatureSnapshotJson NVARCHAR(MAX),
        @PublishedSelectionId BIGINT,
        @Published BIT,
        @PublicationStatus NVARCHAR(20);

    SELECT
        @BotKey = evaluation.BotKey,
        @RunId = evaluation.RunId,
        @AutomationVersion = evaluation.AutomationVersion,
        @PartidoProximoCuotaId = evaluation.PartidoProximoCuotaId,
        @ApiFootballFixtureId = evaluation.ApiFootballFixtureId,
        @Source = evaluation.Source,
        @SourceMatchDate = evaluation.MatchDate,
        @League = evaluation.League,
        @HomeTeam = evaluation.HomeTeam,
        @AwayTeam = evaluation.AwayTeam,
        @SourceMarketType = evaluation.SourceMarketType,
        @MarketType = evaluation.MarketType,
        @LineValue = evaluation.LineValue,
        @Selection = evaluation.SelectedSide,
        @EvaluationSelectedOdds = evaluation.SelectedOdds,
        @Decision = evaluation.Decision,
        @DecisionEngineType = evaluation.DecisionEngineType,
        @ConfigurationVersion = evaluation.ConfigurationVersion,
        @FeatureSchemaVersion = evaluation.FeatureSchemaVersion,
        @BaseModelName = evaluation.BaseModelName,
        @BaseModelVersion = evaluation.BaseModelVersion,
        @BaseModelTrainedThroughUtc = evaluation.BaseModelTrainedThroughUtc,
        @BaseRawProbability = evaluation.BaseRawProbability,
        @BaseCalibratedProbability = evaluation.BaseCalibratedProbability,
        @RawImpliedProbability = evaluation.RawImpliedProbability,
        @MarketNoVigProbability = evaluation.MarketNoVigProbability,
        @FinalProbability = evaluation.FinalProbability,
        @FinalEdge = evaluation.FinalEdge,
        @FinalExpectedValue = evaluation.FinalExpectedValue,
        @SelectionScore = evaluation.RuleBasedConfidenceScore,
        @ContextAgreementScore = evaluation.ContextAgreementScore,
        @DataQualityScore = evaluation.DataQualityScore,
        @DecisionReasonsJson = evaluation.DecisionReasonsJson,
        @RiskFlagsJson = evaluation.RiskFlagsJson,
        @Explanation = evaluation.Explanation,
        @FeatureSnapshotJson = evaluation.FeatureSnapshotJson,
        @PublishedSelectionId = evaluation.PublishedSelectionId,
        @Published = evaluation.Published,
        @PublicationStatus = evaluation.PublicationStatus
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.AutomatedBotPickEvaluationId = @SourceEvaluationId;

    IF @BotKey IS NULL OR @BotKey <> N'H2026'
        RETURN;

    IF @PublishedSelectionId IS NOT NULL OR ISNULL(@Published, 0) = 1 OR @PublicationStatus = N'Published'
        THROW 52109, 'H2026 is shadow-only and cannot reference a published selection.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.AutomatedBotDefinitions WITH (UPDLOCK, HOLDLOCK)
        WHERE BotKey = N'H2026' AND PublishEnabled = 0
    )
        THROW 52110, 'H2026 definition is missing or publication is enabled.', 1;

    -- Pending/invalid inputs remain in the generic audit, but are not calibration
    -- observations because they have no selected immutable quote to settle.
    IF @Decision NOT IN (N'Approved', N'Rejected')
        RETURN;

    IF @RunId IS NULL
       OR @RunId = CONVERT(UNIQUEIDENTIFIER, N'00000000-0000-0000-0000-000000000000')
       OR RIGHT(UPPER(LTRIM(RTRIM(ISNULL(@AutomationVersion, N'')))), 6) <> N'-H2026'
       OR @PartidoProximoCuotaId IS NULL OR @PartidoProximoCuotaId <= 0
       OR @MarketType IS NULL
       OR @MarketType NOT IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
       OR @Selection IS NULL OR @Selection NOT IN (N'Over', N'Under')
       OR @EvaluationSelectedOdds IS NULL OR @EvaluationSelectedOdds <= 1
       OR @LineValue IS NULL OR @LineValue < 0 OR @LineValue * 4 <> FLOOR(@LineValue * 4)
       OR ISNULL(ISJSON(@FeatureSnapshotJson), 0) <> 1
       OR ISNULL(ISJSON(@DecisionReasonsJson), 0) <> 1
       OR ISNULL(ISJSON(@RiskFlagsJson), 0) <> 1
       OR @ConfigurationVersion IS NULL
       OR @ConfigurationVersion NOT LIKE N'bot-h-corners-calibration-shadow-%'
       OR JSON_VALUE(@FeatureSnapshotJson, N'$.configurationVersion') IS NULL
       OR JSON_VALUE(@FeatureSnapshotJson, N'$.configurationVersion') <> @ConfigurationVersion
       OR @FeatureSchemaVersion IS NULL
       OR JSON_VALUE(@FeatureSnapshotJson, N'$.featureSchemaVersion') IS NULL
       OR JSON_VALUE(@FeatureSnapshotJson, N'$.featureSchemaVersion') <> @FeatureSchemaVersion
    BEGIN
        IF @Strict = 1
            THROW 52111, 'H2026 evaluation contract is incomplete or inconsistent.', 1;
        RETURN;
    END;

    DECLARE @PredictionOffset DATETIMEOFFSET(3) = TRY_CONVERT(
        DATETIMEOFFSET(3), JSON_VALUE(@FeatureSnapshotJson, N'$.predictionTimestampUtc'), 127);
    DECLARE @FixtureOffset DATETIMEOFFSET(3) = TRY_CONVERT(
        DATETIMEOFFSET(3), JSON_VALUE(@FeatureSnapshotJson, N'$.asOfDateUtc'), 127);
    DECLARE @PredictionTimestampUtc DATETIME2(3) = CASE WHEN @PredictionOffset IS NULL THEN NULL
        ELSE CONVERT(DATETIME2(3), SWITCHOFFSET(@PredictionOffset, N'+00:00')) END;
    DECLARE @FixtureDateUtc DATETIME2(3) = CASE WHEN @FixtureOffset IS NULL THEN NULL
        ELSE CONVERT(DATETIME2(3), SWITCHOFFSET(@FixtureOffset, N'+00:00')) END;
    DECLARE @OppositeOdds DECIMAL(18,6) = TRY_CONVERT(
        DECIMAL(18,6), JSON_VALUE(@FeatureSnapshotJson, N'$.market.oppositeOdds'));
    DECLARE @FeatureSelectedOdds DECIMAL(18,6) = TRY_CONVERT(
        DECIMAL(18,6), JSON_VALUE(@FeatureSnapshotJson, N'$.market.selectedOdds'));
    DECLARE @FeatureLineValue DECIMAL(6,2) = TRY_CONVERT(
        DECIMAL(6,2), JSON_VALUE(@FeatureSnapshotJson, N'$.market.line'));
    DECLARE @FeatureSelectedSide NVARCHAR(10) = JSON_VALUE(
        @FeatureSnapshotJson, N'$.market.selectedSide');
    DECLARE @FeatureMarketType NVARCHAR(50) = JSON_VALUE(
        @FeatureSnapshotJson, N'$.market.marketType');

    IF @PredictionTimestampUtc IS NULL
       OR @FixtureDateUtc IS NULL
       OR @PredictionTimestampUtc >= @FixtureDateUtc
       OR @PredictionTimestampUtc > DATEADD(MINUTE, 1, SYSUTCDATETIME())
       OR ABS(DATEDIFF(DAY, @SourceMatchDate, @FixtureDateUtc)) > 1
       OR (@ApiFootballFixtureId IS NOT NULL AND @ApiFootballFixtureId <= 0)
       OR @FeatureSelectedOdds IS NULL OR @FeatureSelectedOdds <> @EvaluationSelectedOdds
       OR @FeatureLineValue IS NULL OR @FeatureLineValue <> @LineValue
       OR @FeatureSelectedSide IS NULL OR @FeatureSelectedSide <> @Selection
       OR @FeatureMarketType IS NULL OR @FeatureMarketType <> @MarketType
       OR (@BaseModelTrainedThroughUtc IS NOT NULL
           AND @BaseModelTrainedThroughUtc >= @PredictionTimestampUtc)
    BEGIN
        IF @Strict = 1
            THROW 52112, 'H2026 evaluation has invalid prediction/model temporal lineage.', 1;
        RETURN;
    END;

    DECLARE @QuoteRowFound BIT = 0;
    DECLARE @ExpectedSourceMatchId NVARCHAR(100);
    SELECT TOP (1)
        @QuoteRowFound = 1,
        @ExpectedSourceMatchId = quote.SourceMatchId
    FROM dbo.PartidosProximosCuotas AS quote WITH (HOLDLOCK)
    WHERE quote.PartidoProximoCuotaId = @PartidoProximoCuotaId
      AND quote.Source = @Source
      AND quote.MatchDate = @SourceMatchDate
      AND quote.MarketType = @SourceMarketType
      AND quote.LineValue = @LineValue
      AND COALESCE(NULLIF(quote.StandardizedHomeTeam, N''), quote.HomeTeam)
            COLLATE Latin1_General_100_CI_AI = @HomeTeam COLLATE Latin1_General_100_CI_AI
      AND COALESCE(NULLIF(quote.StandardizedAwayTeam, N''), quote.AwayTeam)
            COLLATE Latin1_General_100_CI_AI = @AwayTeam COLLATE Latin1_General_100_CI_AI;

    IF @QuoteRowFound = 0
    BEGIN
        IF @Strict = 1
            THROW 52128, 'H2026 evaluation does not match its source quote identity.', 1;
        RETURN;
    END;

    DECLARE
        @OddsSnapshotId BIGINT,
        @OddsCapturedAtUtc DATETIME2(3),
        @SnapshotSourceMatchId NVARCHAR(100),
        @SnapshotOverOdds DECIMAL(18,6),
        @SnapshotUnderOdds DECIMAL(18,6);

    SELECT TOP (1)
        @OddsSnapshotId = snapshot.CornerOddsSnapshotId,
        @OddsCapturedAtUtc = snapshot.CapturedAtUtc,
        @SnapshotSourceMatchId = snapshot.SourceMatchId,
        @SnapshotOverOdds = snapshot.OverOdds,
        @SnapshotUnderOdds = snapshot.UnderOdds
    FROM dbo.CornerOddsSnapshots AS snapshot WITH (HOLDLOCK)
    WHERE snapshot.Source = @Source
      AND snapshot.MatchDate = @SourceMatchDate
      AND snapshot.MarketType = @SourceMarketType
      AND snapshot.LineValue = @LineValue
      AND COALESCE(NULLIF(snapshot.StandardizedHomeTeam, N''), snapshot.HomeTeam)
            COLLATE Latin1_General_100_CI_AI = @HomeTeam COLLATE Latin1_General_100_CI_AI
      AND COALESCE(NULLIF(snapshot.StandardizedAwayTeam, N''), snapshot.AwayTeam)
            COLLATE Latin1_General_100_CI_AI = @AwayTeam COLLATE Latin1_General_100_CI_AI
      AND snapshot.CapturedAtUtc <= @PredictionTimestampUtc
      AND
      (
          NULLIF(LTRIM(RTRIM(@ExpectedSourceMatchId)), N'') IS NULL
          OR snapshot.SourceMatchId = @ExpectedSourceMatchId
      )
      AND
      (
          (@Selection = N'Over' AND snapshot.OverOdds = @EvaluationSelectedOdds)
          OR (@Selection = N'Under' AND snapshot.UnderOdds = @EvaluationSelectedOdds)
      )
      AND
      (
          @OppositeOdds IS NULL
          OR (@Selection = N'Over' AND snapshot.UnderOdds = @OppositeOdds)
          OR (@Selection = N'Under' AND snapshot.OverOdds = @OppositeOdds)
      )
    ORDER BY snapshot.CapturedAtUtc DESC, snapshot.CornerOddsSnapshotId DESC;

    IF @OddsSnapshotId IS NULL OR @OddsCapturedAtUtc > @PredictionTimestampUtc
    BEGIN
        IF @Strict = 1
            THROW 52113, 'H2026 evaluation has no exact immutable pre-decision odds snapshot.', 1;
        RETURN;
    END;

    DECLARE @SelectedSnapshotOdds DECIMAL(18,6) = CASE @Selection
        WHEN N'Over' THEN @SnapshotOverOdds ELSE @SnapshotUnderOdds END;
    DECLARE @ComputedRawImplied FLOAT = 1.0 / CONVERT(FLOAT, @SelectedSnapshotOdds);
    DECLARE @ComputedNoVig FLOAT = CASE
        WHEN @SnapshotOverOdds > 1 AND @SnapshotUnderOdds > 1 THEN
            @ComputedRawImplied /
            ((1.0 / CONVERT(FLOAT, @SnapshotOverOdds)) + (1.0 / CONVERT(FLOAT, @SnapshotUnderOdds)))
        ELSE NULL END;

    IF @RawImpliedProbability IS NULL
       OR ABS(CONVERT(FLOAT, @RawImpliedProbability) - @ComputedRawImplied) > 0.000020
       OR (@Decision = N'Approved'
           AND (@SnapshotOverOdds IS NULL OR @SnapshotOverOdds <= 1
                OR @SnapshotUnderOdds IS NULL OR @SnapshotUnderOdds <= 1
                OR @MarketNoVigProbability IS NULL))
       OR (@MarketNoVigProbability IS NOT NULL
           AND (@ComputedNoVig IS NULL
                OR ABS(CONVERT(FLOAT, @MarketNoVigProbability) - @ComputedNoVig) > 0.000020))
       OR @FinalProbability IS NULL OR @FinalProbability <= 0 OR @FinalProbability >= 1
       OR @FinalEdge IS NULL OR @FinalExpectedValue IS NULL
       OR (@MarketNoVigProbability IS NOT NULL
           AND ABS(CONVERT(FLOAT, @FinalEdge)
                   - (CONVERT(FLOAT, @FinalProbability) - CONVERT(FLOAT, @MarketNoVigProbability))) > 0.000050)
       OR ABS(CONVERT(FLOAT, @FinalExpectedValue)
              - ((CONVERT(FLOAT, @FinalProbability) * CONVERT(FLOAT, @SelectedSnapshotOdds)) - 1.0)) > 0.000050
       OR (@Decision = N'Approved'
           AND (@SelectionScore IS NULL OR @ContextAgreementScore IS NULL OR @DataQualityScore IS NULL
                OR @SelectionScore NOT BETWEEN 0 AND 1
                OR @ContextAgreementScore NOT BETWEEN 0 AND 1
                OR @DataQualityScore NOT BETWEEN 0 AND 1))
    BEGIN
        IF @Strict = 1
            THROW 52114, 'H2026 probabilities/EV do not match the immutable decision-time quote.', 1;
        RETURN;
    END;

    DECLARE @VirtualStakeUnits DECIMAL(9,4);
    SELECT @VirtualStakeUnits = StakeMultiplier
    FROM dbo.AutomatedBotDefinitions
    WHERE BotKey = N'H2026' AND PublishEnabled = 0;
    IF @VirtualStakeUnits IS NULL OR @VirtualStakeUnits <= 0 OR @VirtualStakeUnits > 10
        THROW 52115, 'H2026 virtual shadow stake is invalid.', 1;

    DECLARE @FeatureSnapshotHash BINARY(32) = HASHBYTES(N'SHA2_256', @FeatureSnapshotJson);
    DECLARE @CaptureKey CHAR(64) = LOWER(CONVERT(VARCHAR(64), HASHBYTES(
        N'SHA2_256', CONCAT(
            @SourceEvaluationId, N'|', @OddsSnapshotId, N'|',
            CONVERT(VARCHAR(64), @FeatureSnapshotHash, 2))), 2));

    INSERT INTO dbo.BotH2026ShadowEvaluations
    (
        CaptureKey, SourceEvaluationId, RunId, BotKey, AutomationVersion,
        PartidoProximoCuotaId, OddsSnapshotId, OddsCapturedAtUtc,
        PredictionTimestampUtc, FixtureDateUtc, ApiFootballFixtureId,
        Source, SourceMatchId, SourceMatchDate, League, HomeTeam, AwayTeam,
        SourceMarketType, MarketType, LineValue, Selection, OverOdds,
        UnderOdds, SelectedOdds, Decision, DecisionEngineType,
        ConfigurationVersion, FeatureSchemaVersion, BaseModelName,
        BaseModelVersion, BaseModelTrainedThroughUtc, BaseRawProbability,
        BaseCalibratedProbability, RawImpliedProbability,
        MarketNoVigProbability, FinalProbability, FinalEdge,
        FinalExpectedValue, SelectionScore, ContextAgreementScore,
        DataQualityScore, VirtualStakeUnits, DecisionReasonsJson,
        RiskFlagsJson, Explanation, FeatureSnapshotJson, FeatureSnapshotHash
    )
    SELECT
        @CaptureKey, @SourceEvaluationId, @RunId, N'H2026', @AutomationVersion,
        @PartidoProximoCuotaId, @OddsSnapshotId, @OddsCapturedAtUtc,
        @PredictionTimestampUtc, @FixtureDateUtc, @ApiFootballFixtureId,
        @Source, @SnapshotSourceMatchId, @SourceMatchDate, @League,
        @HomeTeam, @AwayTeam, @SourceMarketType, @MarketType,
        @LineValue, @Selection, @SnapshotOverOdds, @SnapshotUnderOdds,
        @SelectedSnapshotOdds, @Decision, @DecisionEngineType,
        @ConfigurationVersion, @FeatureSchemaVersion, @BaseModelName,
        @BaseModelVersion, @BaseModelTrainedThroughUtc, @BaseRawProbability,
        @BaseCalibratedProbability, @RawImpliedProbability,
        @MarketNoVigProbability, @FinalProbability, @FinalEdge,
        @FinalExpectedValue, @SelectionScore, @ContextAgreementScore,
        @DataQualityScore, @VirtualStakeUnits,
        COALESCE(@DecisionReasonsJson, N'[]'), COALESCE(@RiskFlagsJson, N'[]'),
        COALESCE(@Explanation, N''), @FeatureSnapshotJson, @FeatureSnapshotHash
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.BotH2026ShadowEvaluations WITH (UPDLOCK, HOLDLOCK)
        WHERE CaptureKey = @CaptureKey
    );
END;

GO

-- Best-effort capture of pre-migration H decisions.  Rows without provable exact
-- snapshot lineage stay out of the lab and are surfaced by the status endpoint.
DECLARE @LegacySourceEvaluationId BIGINT;
DECLARE BotHLegacyCapture CURSOR LOCAL FAST_FORWARD FOR
    SELECT evaluation.AutomatedBotPickEvaluationId
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'H2026'
      AND evaluation.Decision IN (N'Approved', N'Rejected')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.BotH2026ShadowEvaluations AS captured
          WHERE captured.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId
      )
    ORDER BY evaluation.AutomatedBotPickEvaluationId;
OPEN BotHLegacyCapture;
FETCH NEXT FROM BotHLegacyCapture INTO @LegacySourceEvaluationId;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC dbo.sp_CaptureBotH2026ShadowEvaluation
        @SourceEvaluationId = @LegacySourceEvaluationId,
        @Strict = 0;
    FETCH NEXT FROM BotHLegacyCapture INTO @LegacySourceEvaluationId;
END;
CLOSE BotHLegacyCapture;
DEALLOCATE BotHLegacyCapture;

GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedBotPickEvaluations_CaptureH2026Shadow
ON dbo.AutomatedBotPickEvaluations
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE BotKey = N'H2026')
        RETURN;

    IF EXISTS
    (
        SELECT 1 FROM inserted
        WHERE BotKey = N'H2026'
          AND
          (
              PublishedSelectionId IS NOT NULL
              OR ISNULL(Published, 0) = 1
              OR PublicationStatus = N'Published'
          )
    )
        THROW 52116, 'H2026 audit rows cannot carry publication state.', 1;

    DECLARE @SourceEvaluationId BIGINT;
    DECLARE BotHInsertedCapture CURSOR LOCAL FAST_FORWARD FOR
        SELECT AutomatedBotPickEvaluationId
        FROM inserted
        WHERE BotKey = N'H2026'
        ORDER BY AutomatedBotPickEvaluationId;
    OPEN BotHInsertedCapture;
    FETCH NEXT FROM BotHInsertedCapture INTO @SourceEvaluationId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC dbo.sp_CaptureBotH2026ShadowEvaluation
            @SourceEvaluationId = @SourceEvaluationId,
            @Strict = 1;
        FETCH NEXT FROM BotHInsertedCapture INTO @SourceEvaluationId;
    END;
    CLOSE BotHInsertedCapture;
    DEALLOCATE BotHInsertedCapture;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedBotDefinitions_KeepH2026ShadowOnly
ON dbo.AutomatedBotDefinitions
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1 FROM inserted
        WHERE BotKey = N'H2026' AND PublishEnabled <> 0
    )
        THROW 52117, 'H2026 is a permanent shadow-only challenger.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedCornerBetSelections_BlockH2026
ON dbo.AutomatedCornerBetSelections
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE BotKey = N'H2026')
        THROW 52118, 'H2026 cannot create or own published selections.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_BotH2026ShadowEvaluations_Immutable
ON dbo.BotH2026ShadowEvaluations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52119, 'Bot H shadow evidence is append-only and cannot be updated or deleted.', 1;
END;

GO

/*
    Dynamic, reproducible settlement projection.  A result is exposed only when:
      * the copied odds evidence still exactly matches its referenced snapshot;
      * exactly one best identity match exists;
      * official corner statistics are final and available;
      * ApiFootballUpdatedAtUtc proves the outcome appeared after prediction and no
        later than the caller's AsOfUtc.
*/
CREATE OR ALTER FUNCTION dbo.fn_BotH2026ShadowLab
(
    @AsOfUtc DATETIME2(3)
)
RETURNS TABLE
AS
RETURN
(
    WITH Evidence AS
    (
        SELECT
            shadow.*,
            SnapshotLineageState = CASE
                WHEN snapshot.CornerOddsSnapshotId IS NOT NULL
                 AND snapshot.CapturedAtUtc = shadow.OddsCapturedAtUtc
                 AND snapshot.Source = shadow.Source
                 AND snapshot.MatchDate = shadow.SourceMatchDate
                 AND snapshot.MarketType = shadow.SourceMarketType
                 AND snapshot.LineValue = shadow.LineValue
                 AND (snapshot.SourceMatchId = shadow.SourceMatchId
                      OR (snapshot.SourceMatchId IS NULL AND shadow.SourceMatchId IS NULL))
                 AND COALESCE(NULLIF(snapshot.StandardizedHomeTeam, N''), snapshot.HomeTeam)
                        COLLATE Latin1_General_100_CI_AI = shadow.HomeTeam COLLATE Latin1_General_100_CI_AI
                 AND COALESCE(NULLIF(snapshot.StandardizedAwayTeam, N''), snapshot.AwayTeam)
                        COLLATE Latin1_General_100_CI_AI = shadow.AwayTeam COLLATE Latin1_General_100_CI_AI
                 AND (snapshot.OverOdds = shadow.OverOdds OR (snapshot.OverOdds IS NULL AND shadow.OverOdds IS NULL))
                 AND (snapshot.UnderOdds = shadow.UnderOdds OR (snapshot.UnderOdds IS NULL AND shadow.UnderOdds IS NULL))
                THEN N'Valid' ELSE N'Invalid' END
        FROM dbo.BotH2026ShadowEvaluations AS shadow
        LEFT JOIN dbo.CornerOddsSnapshots AS snapshot
          ON snapshot.CornerOddsSnapshotId = shadow.OddsSnapshotId
        WHERE shadow.PredictionTimestampUtc <= @AsOfUtc
    ),
    IdentityCandidates AS
    (
        SELECT
            evidence.ShadowEvaluationId,
            MatchHistoryId = CONVERT(BIGINT, history.Id),
            LinkPriority = 0,
            DateDistanceDays = ABS(DATEDIFF(DAY, evidence.SourceMatchDate, history.MatchDate))
        FROM Evidence AS evidence
        INNER JOIN dbo.MatchHistory AS history
          ON history.ApiFootballFixtureId = evidence.ApiFootballFixtureId
        WHERE evidence.ApiFootballFixtureId IS NOT NULL

        UNION ALL

        SELECT
            evidence.ShadowEvaluationId,
            MatchHistoryId = CONVERT(BIGINT, history.Id),
            LinkPriority = 1,
            DateDistanceDays = ABS(DATEDIFF(DAY, evidence.SourceMatchDate, history.MatchDate))
        FROM Evidence AS evidence
        INNER JOIN dbo.MatchHistory AS history
          ON history.MatchDate BETWEEN DATEADD(DAY, -1, CAST(evidence.SourceMatchDate AS DATE))
                                   AND DATEADD(DAY, 1, CAST(evidence.SourceMatchDate AS DATE))
         AND COALESCE(NULLIF(history.StandardizedHomeTeam, N''), history.HomeTeam)
                COLLATE Latin1_General_100_CI_AI = evidence.HomeTeam COLLATE Latin1_General_100_CI_AI
         AND COALESCE(NULLIF(history.StandardizedAwayTeam, N''), history.AwayTeam)
                COLLATE Latin1_General_100_CI_AI = evidence.AwayTeam COLLATE Latin1_General_100_CI_AI
        WHERE evidence.ApiFootballFixtureId IS NULL
          AND history.ApiFootballFixtureId IS NOT NULL
    ),
    RankedIdentityCandidates AS
    (
        SELECT
            candidate.*,
            CandidateRank = DENSE_RANK() OVER
            (
                PARTITION BY candidate.ShadowEvaluationId
                ORDER BY candidate.LinkPriority, candidate.DateDistanceDays
            )
        FROM IdentityCandidates AS candidate
    ),
    MatchedIdentity AS
    (
        SELECT
            ShadowEvaluationId,
            MatchCandidateCount = SUM(CASE WHEN CandidateRank = 1 THEN 1 ELSE 0 END),
            MatchHistoryId = MAX(CASE WHEN CandidateRank = 1 THEN MatchHistoryId END),
            MatchLinkMethod = CASE MIN(CASE WHEN CandidateRank = 1 THEN LinkPriority END)
                WHEN 0 THEN N'ApiFootballFixtureId'
                WHEN 1 THEN N'CanonicalTeamsAndDate'
            END
        FROM RankedIdentityCandidates
        GROUP BY ShadowEvaluationId
    ),
    OutcomeEvidence AS
    (
        SELECT
            evidence.*,
            MatchCandidateCount = COALESCE(matched.MatchCandidateCount, 0),
            matched.MatchHistoryId,
            matched.MatchLinkMethod,
            MatchedFixtureId = history.ApiFootballFixtureId,
            history.FixtureStatus,
            history.ApiFootballCornersAvailable,
            history.ApiFootballUpdatedAtUtc,
            ActualHomeCorners = history.HomeCorners,
            ActualAwayCorners = history.AwayCorners,
            IdentityMatches = CASE
                WHEN history.Id IS NOT NULL
                 AND ABS(DATEDIFF(DAY, evidence.SourceMatchDate, history.MatchDate)) <= 1
                 AND COALESCE(NULLIF(history.StandardizedHomeTeam, N''), history.HomeTeam)
                        COLLATE Latin1_General_100_CI_AI = evidence.HomeTeam COLLATE Latin1_General_100_CI_AI
                 AND COALESCE(NULLIF(history.StandardizedAwayTeam, N''), history.AwayTeam)
                        COLLATE Latin1_General_100_CI_AI = evidence.AwayTeam COLLATE Latin1_General_100_CI_AI
                THEN 1 ELSE 0 END
        FROM Evidence AS evidence
        LEFT JOIN MatchedIdentity AS matched
          ON matched.ShadowEvaluationId = evidence.ShadowEvaluationId
        LEFT JOIN dbo.MatchHistory AS history
          ON history.Id = matched.MatchHistoryId
         AND matched.MatchCandidateCount = 1
    ),
    Classified AS
    (
        SELECT
            outcome.*,
            OutcomeAvailableUtc = outcome.ApiFootballUpdatedAtUtc,
            ActualValue = CONVERT(INT, CASE outcome.MarketType
                WHEN N'TotalCorners' THEN outcome.ActualHomeCorners + outcome.ActualAwayCorners
                WHEN N'HomeTeamCorners' THEN outcome.ActualHomeCorners
                WHEN N'AwayTeamCorners' THEN outcome.ActualAwayCorners
            END),
            SettlementState = CASE
                WHEN outcome.SnapshotLineageState <> N'Valid' THEN N'SnapshotInvalid'
                WHEN outcome.MatchCandidateCount = 0 THEN
                    CASE WHEN @AsOfUtc < outcome.FixtureDateUtc THEN N'Pending' ELSE N'Unmatched' END
                WHEN outcome.MatchCandidateCount > 1 THEN N'Ambiguous'
                WHEN outcome.IdentityMatches <> 1 THEN N'IdentityMismatch'
                WHEN UPPER(LTRIM(RTRIM(COALESCE(outcome.FixtureStatus, N'')))) NOT IN (N'FT', N'AET', N'PEN')
                    THEN N'Pending'
                WHEN ISNULL(outcome.ApiFootballCornersAvailable, 0) <> 1
                     OR outcome.ActualHomeCorners IS NULL OR outcome.ActualAwayCorners IS NULL
                    THEN N'Pending'
                WHEN outcome.ApiFootballUpdatedAtUtc IS NULL THEN N'OutcomeTimestampMissing'
                WHEN outcome.ApiFootballUpdatedAtUtc <= outcome.PredictionTimestampUtc THEN N'TemporalRejected'
                WHEN outcome.ApiFootballUpdatedAtUtc > @AsOfUtc THEN N'Pending'
                ELSE N'Settled'
            END
        FROM OutcomeEvidence AS outcome
    ),
    SplitLines AS
    (
        SELECT
            classified.*,
            FirstLine = CASE WHEN classified.LineValue - FLOOR(classified.LineValue) IN (0.25, 0.75)
                THEN classified.LineValue - 0.25 ELSE classified.LineValue END,
            SecondLine = CASE WHEN classified.LineValue - FLOOR(classified.LineValue) IN (0.25, 0.75)
                THEN classified.LineValue + 0.25 ELSE classified.LineValue END
        FROM Classified AS classified
    ),
    Factorized AS
    (
        SELECT
            split.*,
            SettlementFactor = CASE WHEN split.SettlementState <> N'Settled' THEN NULL ELSE
                CONVERT(DECIMAL(9,4),
                (
                    CONVERT(DECIMAL(9,4), CASE split.Selection
                        WHEN N'Over' THEN CASE WHEN split.ActualValue > split.FirstLine THEN 1.0
                                             WHEN split.ActualValue = split.FirstLine THEN 0.0 ELSE -1.0 END
                        WHEN N'Under' THEN CASE WHEN split.ActualValue < split.FirstLine THEN 1.0
                                              WHEN split.ActualValue = split.FirstLine THEN 0.0 ELSE -1.0 END
                    END)
                    +
                    CONVERT(DECIMAL(9,4), CASE split.Selection
                        WHEN N'Over' THEN CASE WHEN split.ActualValue > split.SecondLine THEN 1.0
                                             WHEN split.ActualValue = split.SecondLine THEN 0.0 ELSE -1.0 END
                        WHEN N'Under' THEN CASE WHEN split.ActualValue < split.SecondLine THEN 1.0
                                              WHEN split.ActualValue = split.SecondLine THEN 0.0 ELSE -1.0 END
                    END)
                ) / 2.0) END
        FROM SplitLines AS split
    )
    SELECT
        factor.ShadowEvaluationId,
        factor.SourceEvaluationId,
        factor.CaptureKey,
        factor.RunId,
        factor.BotKey,
        factor.AutomationVersion,
        factor.PartidoProximoCuotaId,
        factor.OddsSnapshotId,
        factor.OddsCapturedAtUtc,
        factor.PredictionTimestampUtc,
        factor.FixtureDateUtc,
        factor.ApiFootballFixtureId,
        factor.Source,
        factor.SourceMatchId,
        factor.SourceMatchDate,
        factor.League,
        factor.HomeTeam,
        factor.AwayTeam,
        factor.SourceMarketType,
        factor.MarketType,
        factor.LineValue,
        factor.Selection,
        factor.OverOdds,
        factor.UnderOdds,
        factor.SelectedOdds,
        factor.Decision,
        factor.DecisionEngineType,
        factor.ConfigurationVersion,
        factor.FeatureSchemaVersion,
        factor.BaseModelName,
        factor.BaseModelVersion,
        factor.BaseModelTrainedThroughUtc,
        factor.BaseRawProbability,
        factor.BaseCalibratedProbability,
        factor.RawImpliedProbability,
        factor.MarketNoVigProbability,
        factor.FinalProbability,
        factor.FinalEdge,
        factor.FinalExpectedValue,
        factor.SelectionScore,
        factor.ContextAgreementScore,
        factor.DataQualityScore,
        factor.VirtualStakeUnits,
        factor.DecisionReasonsJson,
        factor.RiskFlagsJson,
        factor.Explanation,
        factor.FeatureSnapshotJson,
        factor.SnapshotLineageState,
        factor.MatchCandidateCount,
        factor.MatchHistoryId,
        factor.MatchLinkMethod,
        factor.OutcomeAvailableUtc,
        factor.ActualHomeCorners,
        factor.ActualAwayCorners,
        factor.ActualValue,
        factor.SettlementState,
        factor.SettlementFactor,
        Result = CASE factor.SettlementFactor
            WHEN 1.0000 THEN N'Win'
            WHEN 0.5000 THEN N'HalfWin'
            WHEN 0.0000 THEN N'Push'
            WHEN -0.5000 THEN N'HalfLoss'
            WHEN -1.0000 THEN N'Loss'
        END,
        ProfitLoss = CONVERT(DECIMAL(12,4), CASE factor.SettlementFactor
            WHEN 1.0000 THEN factor.VirtualStakeUnits * (factor.SelectedOdds - 1.0)
            WHEN 0.5000 THEN factor.VirtualStakeUnits * (factor.SelectedOdds - 1.0) / 2.0
            WHEN 0.0000 THEN 0.0
            WHEN -0.5000 THEN -factor.VirtualStakeUnits / 2.0
            WHEN -1.0000 THEN -factor.VirtualStakeUnits
        END),
        -- Same economic-equivalent target used by Bot E/H calibration:
        -- (unit return + 1) / decision-time odds.  This correctly makes a push
        -- worth 1/odds and half results odds-dependent.
        EconomicOutcome = CONVERT(DECIMAL(9,6), CASE factor.SettlementFactor
            WHEN 1.0000 THEN 1.0
            WHEN 0.5000 THEN (((factor.SelectedOdds - 1.0) / 2.0) + 1.0) / factor.SelectedOdds
            WHEN 0.0000 THEN 1.0 / factor.SelectedOdds
            WHEN -0.5000 THEN 0.5 / factor.SelectedOdds
            WHEN -1.0000 THEN 0.0
        END),
        factor.CapturedAtUtc
    FROM Factorized AS factor
);

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotH2026ShadowEvaluations
    @PredictionFromUtc DATETIME2(3) = NULL,
    @PredictionToUtc DATETIME2(3) = NULL,
    @AsOfUtc DATETIME2(3) = NULL,
    @Decision NVARCHAR(20) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @Selection NVARCHAR(10) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL,
    @SettlementState NVARCHAR(40) = NULL,
    @Page INT = 1,
    @PageSize INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET @AsOfUtc = COALESCE(@AsOfUtc, SYSUTCDATETIME());

    IF @AsOfUtc > DATEADD(MINUTE, 1, SYSUTCDATETIME())
        THROW 52120, 'AsOfUtc cannot be in the future.', 1;
    IF @PredictionFromUtc IS NOT NULL AND @PredictionToUtc IS NOT NULL
       AND @PredictionToUtc <= @PredictionFromUtc
        THROW 52121, 'PredictionToUtc must be later than PredictionFromUtc.', 1;
    IF @Page < 1 OR @Page > 1000000 OR @PageSize < 1 OR @PageSize > 1000
        THROW 52122, 'Invalid Bot H shadow-lab pagination.', 1;
    IF @Decision IS NOT NULL AND @Decision NOT IN (N'Approved', N'Rejected')
        THROW 52123, 'Invalid Bot H decision filter.', 1;
    IF @MarketType IS NOT NULL
       AND @MarketType NOT IN (N'TotalCorners', N'HomeTeamCorners', N'AwayTeamCorners')
        THROW 52124, 'Invalid Bot H market filter.', 1;
    IF @Selection IS NOT NULL AND @Selection NOT IN (N'Over', N'Under')
        THROW 52125, 'Invalid Bot H selection filter.', 1;
    IF @SettlementState IS NOT NULL
       AND @SettlementState NOT IN
       (
           N'Settled', N'Pending', N'Unmatched', N'Ambiguous',
           N'IdentityMismatch', N'OutcomeTimestampMissing',
           N'TemporalRejected', N'SnapshotInvalid'
       )
        THROW 52130, 'Invalid Bot H settlement-state filter.', 1;

    -- Keep the list endpoint lightweight. The append-only evidence JSON remains
    -- in SQL for audits, but the dashboard table does not display or need it.
    SELECT
        lab.ShadowEvaluationId,
        lab.SourceEvaluationId,
        lab.CaptureKey,
        lab.RunId,
        lab.BotKey,
        lab.AutomationVersion,
        lab.PartidoProximoCuotaId,
        lab.OddsSnapshotId,
        lab.OddsCapturedAtUtc,
        lab.PredictionTimestampUtc,
        lab.FixtureDateUtc,
        lab.ApiFootballFixtureId,
        lab.Source,
        lab.SourceMatchId,
        lab.SourceMatchDate,
        lab.League,
        lab.HomeTeam,
        lab.AwayTeam,
        lab.SourceMarketType,
        lab.MarketType,
        lab.LineValue,
        lab.Selection,
        lab.OverOdds,
        lab.UnderOdds,
        lab.SelectedOdds,
        lab.Decision,
        lab.DecisionEngineType,
        lab.ConfigurationVersion,
        lab.FeatureSchemaVersion,
        lab.BaseModelName,
        lab.BaseModelVersion,
        lab.BaseModelTrainedThroughUtc,
        lab.BaseRawProbability,
        lab.BaseCalibratedProbability,
        lab.RawImpliedProbability,
        lab.MarketNoVigProbability,
        lab.FinalProbability,
        lab.FinalEdge,
        lab.FinalExpectedValue,
        lab.SelectionScore,
        lab.ContextAgreementScore,
        lab.DataQualityScore,
        lab.VirtualStakeUnits,
        lab.Explanation,
        lab.SnapshotLineageState,
        lab.MatchCandidateCount,
        lab.MatchHistoryId,
        lab.MatchLinkMethod,
        lab.OutcomeAvailableUtc,
        lab.ActualHomeCorners,
        lab.ActualAwayCorners,
        lab.ActualValue,
        lab.SettlementState,
        lab.SettlementFactor,
        lab.Result,
        lab.ProfitLoss,
        lab.EconomicOutcome,
        lab.CapturedAtUtc,
        TotalRows = COUNT_BIG(*) OVER ()
    FROM dbo.fn_BotH2026ShadowLab(@AsOfUtc) AS lab
    WHERE (@PredictionFromUtc IS NULL OR lab.PredictionTimestampUtc >= @PredictionFromUtc)
      AND (@PredictionToUtc IS NULL OR lab.PredictionTimestampUtc < @PredictionToUtc)
      AND (@Decision IS NULL OR lab.Decision = @Decision)
      AND (@MarketType IS NULL OR lab.MarketType = @MarketType)
      AND (@Selection IS NULL OR lab.Selection = @Selection)
      AND (@ConfigurationVersion IS NULL OR lab.ConfigurationVersion = @ConfigurationVersion)
      AND (@SettlementState IS NULL OR lab.SettlementState = @SettlementState)
    ORDER BY lab.PredictionTimestampUtc DESC, lab.ShadowEvaluationId DESC
    OFFSET CONVERT(BIGINT, @Page - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotH2026ShadowScorecards
    @AsOfUtc DATETIME2(3) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @AsOfUtc = COALESCE(@AsOfUtc, SYSUTCDATETIME());
    IF @AsOfUtc > DATEADD(MINUTE, 1, SYSUTCDATETIME())
        THROW 52126, 'AsOfUtc cannot be in the future.', 1;

    DECLARE @Windows TABLE(WindowDays INT NOT NULL PRIMARY KEY);
    INSERT INTO @Windows(WindowDays) VALUES (7), (30), (90);

    SELECT * INTO #BotHLab
    FROM dbo.fn_BotH2026ShadowLab(@AsOfUtc) AS lab
    WHERE lab.FixtureDateUtc >= DATEADD(DAY, -90, @AsOfUtc)
      AND lab.FixtureDateUtc <= @AsOfUtc
      AND (@ConfigurationVersion IS NULL OR lab.ConfigurationVersion = @ConfigurationVersion)
    OPTION (RECOMPILE);

    DECLARE @Scorecards TABLE
    (
        WindowDays INT NOT NULL,
        DateFromUtc DATETIME2(3) NOT NULL,
        DateToUtc DATETIME2(3) NOT NULL,
        Dimension NVARCHAR(30) NOT NULL,
        Segment NVARCHAR(250) NOT NULL,
        ConfigurationVersion NVARCHAR(80) NULL,
        MarketType NVARCHAR(50) NULL,
        Selection NVARCHAR(10) NULL,
        Evaluations BIGINT NOT NULL,
        FixturesEvaluated BIGINT NOT NULL,
        ApprovedSignals BIGINT NOT NULL,
        Approved BIGINT NOT NULL,
        Rejected BIGINT NOT NULL,
        SafelySettled BIGINT NOT NULL,
        UnsafeOrUnavailable BIGINT NOT NULL,
        Won BIGINT NOT NULL,
        HalfWon BIGINT NOT NULL,
        Pushes BIGINT NOT NULL,
        HalfLost BIGINT NOT NULL,
        Lost BIGINT NOT NULL,
        Stake FLOAT NULL,
        ProfitLoss FLOAT NULL,
        Yield FLOAT NULL,
        AverageModelProbability FLOAT NULL,
        AverageMarketProbability FLOAT NULL,
        ObservedEconomicOutcome FLOAT NULL,
        CalibrationGap FLOAT NULL,
        Brier FLOAT NULL,
        MarketBrier FLOAT NULL,
        DeltaBrier FLOAT NULL,
        AverageEdge FLOAT NULL,
        AverageExpectedValue FLOAT NULL,
        CoverageRate FLOAT NULL,
        Deployable BIT NOT NULL,
        PromotionState NVARCHAR(30) NOT NULL,
        UnitOfAnalysis NVARCHAR(80) NOT NULL
    );

    ;WITH LabIdentity AS
    (
        SELECT
            lab.*,
            FixtureKey = CASE WHEN lab.ApiFootballFixtureId IS NOT NULL
                THEN CONCAT(N'AF|', CONVERT(NVARCHAR(30), lab.ApiFootballFixtureId))
                ELSE CONCAT(N'SRC|', lab.Source, N'|', COALESCE(lab.SourceMatchId, N''), N'|',
                    CONVERT(NVARCHAR(19), lab.SourceMatchDate, 126), N'|', lab.HomeTeam, N'|', lab.AwayTeam)
                END
        FROM #BotHLab AS lab
    ),
    RankedLab AS
    (
        SELECT
            lab.*,
            ApprovedSequence = SUM(CASE WHEN lab.Decision = N'Approved' THEN 1 ELSE 0 END) OVER
            (
                PARTITION BY lab.ConfigurationVersion, lab.FixtureKey
                ORDER BY lab.PredictionTimestampUtc, lab.ShadowEvaluationId
                ROWS UNBOUNDED PRECEDING
            )
        FROM LabIdentity AS lab
    ),
    Expanded AS
    (
        SELECT
            window.WindowDays,
            DateFromUtc = DATEADD(DAY, -window.WindowDays, @AsOfUtc),
            DateToUtc = @AsOfUtc,
            lab.*,
            dimension.Dimension,
            dimension.Segment,
            dimension.SegmentConfigurationVersion,
            dimension.SegmentMarketType,
            dimension.SegmentSelection
        FROM @Windows AS window
        INNER JOIN RankedLab AS lab
          ON lab.FixtureDateUtc >= DATEADD(DAY, -window.WindowDays, @AsOfUtc)
         AND lab.FixtureDateUtc <= @AsOfUtc
        CROSS APPLY
        (
            VALUES
                (N'Overall', N'All', CONVERT(NVARCHAR(80), NULL), CONVERT(NVARCHAR(50), NULL), CONVERT(NVARCHAR(10), NULL)),
                (N'Configuration', lab.ConfigurationVersion, lab.ConfigurationVersion, NULL, NULL),
                (N'MarketType', lab.MarketType, NULL, lab.MarketType, NULL),
                (N'MarketSide', CONCAT(lab.MarketType, N' · ', lab.Selection), NULL, lab.MarketType, lab.Selection)
        ) AS dimension(Dimension, Segment, SegmentConfigurationVersion, SegmentMarketType, SegmentSelection)
    ),
    Aggregated AS
    (
        SELECT
            WindowDays,
            DateFromUtc,
            DateToUtc,
            Dimension,
            Segment,
            ConfigurationVersion = SegmentConfigurationVersion,
            MarketType = SegmentMarketType,
            Selection = SegmentSelection,
            Evaluations = COUNT_BIG(*),
            FixturesEvaluated = COUNT_BIG(DISTINCT FixtureKey),
            ApprovedSignals = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' THEN 1 ELSE 0 END)),
            Approved = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 THEN 1 ELSE 0 END)),
            Rejected = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Rejected' THEN 1 ELSE 0 END)),
            SafelySettled = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled' THEN 1 ELSE 0 END)),
            UnsafeOrUnavailable = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState <> N'Settled' THEN 1 ELSE 0 END)),
            Won = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND Result = N'Win' THEN 1 ELSE 0 END)),
            HalfWon = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND Result = N'HalfWin' THEN 1 ELSE 0 END)),
            Pushes = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND Result = N'Push' THEN 1 ELSE 0 END)),
            HalfLost = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND Result = N'HalfLoss' THEN 1 ELSE 0 END)),
            Lost = SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND Result = N'Loss' THEN 1 ELSE 0 END)),
            Stake = SUM(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN CONVERT(FLOAT, VirtualStakeUnits) END),
            ProfitLoss = SUM(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN CONVERT(FLOAT, ProfitLoss) END),
            AverageModelProbability = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN CONVERT(FLOAT, FinalProbability) END),
            AverageMarketProbability = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN CONVERT(FLOAT, MarketNoVigProbability) END),
            ObservedEconomicOutcome = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN CONVERT(FLOAT, EconomicOutcome) END),
            Brier = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                THEN POWER(CONVERT(FLOAT, FinalProbability) - CONVERT(FLOAT, EconomicOutcome), 2) END),
            MarketBrier = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 AND SettlementState = N'Settled'
                                      AND MarketNoVigProbability IS NOT NULL
                THEN POWER(CONVERT(FLOAT, MarketNoVigProbability) - CONVERT(FLOAT, EconomicOutcome), 2) END),
            AverageEdge = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 THEN CONVERT(FLOAT, FinalEdge) END),
            AverageExpectedValue = AVG(CASE WHEN Decision = N'Approved' AND ApprovedSequence = 1 THEN CONVERT(FLOAT, FinalExpectedValue) END)
        FROM Expanded
        GROUP BY
            WindowDays, DateFromUtc, DateToUtc, Dimension, Segment,
            SegmentConfigurationVersion, SegmentMarketType, SegmentSelection
    )
    INSERT INTO @Scorecards
    SELECT
        aggregated.WindowDays,
        aggregated.DateFromUtc,
        aggregated.DateToUtc,
        aggregated.Dimension,
        aggregated.Segment,
        aggregated.ConfigurationVersion,
        aggregated.MarketType,
        aggregated.Selection,
        aggregated.Evaluations,
        aggregated.FixturesEvaluated,
        aggregated.ApprovedSignals,
        aggregated.Approved,
        aggregated.Rejected,
        aggregated.SafelySettled,
        aggregated.UnsafeOrUnavailable,
        aggregated.Won,
        aggregated.HalfWon,
        aggregated.Pushes,
        aggregated.HalfLost,
        aggregated.Lost,
        aggregated.Stake,
        aggregated.ProfitLoss,
        Yield = aggregated.ProfitLoss / NULLIF(aggregated.Stake, 0),
        aggregated.AverageModelProbability,
        aggregated.AverageMarketProbability,
        aggregated.ObservedEconomicOutcome,
        CalibrationGap = aggregated.AverageModelProbability - aggregated.ObservedEconomicOutcome,
        aggregated.Brier,
        aggregated.MarketBrier,
        DeltaBrier = aggregated.Brier - aggregated.MarketBrier,
        aggregated.AverageEdge,
        aggregated.AverageExpectedValue,
        CoverageRate = CONVERT(FLOAT, aggregated.SafelySettled) / NULLIF(CONVERT(FLOAT, aggregated.Approved), 0),
        Deployable = CONVERT(BIT, 0),
        PromotionState = N'SHADOW_ONLY',
        UnitOfAnalysis = N'FIRST_APPROVED_PER_FIXTURE_CONFIGURATION'
    FROM Aggregated AS aggregated;

    INSERT INTO @Scorecards
    (
        WindowDays, DateFromUtc, DateToUtc, Dimension, Segment,
        Evaluations, FixturesEvaluated, ApprovedSignals, Approved, Rejected, SafelySettled,
        UnsafeOrUnavailable, Won, HalfWon, Pushes, HalfLost, Lost,
        Deployable, PromotionState, UnitOfAnalysis
    )
    SELECT
        window.WindowDays,
        DATEADD(DAY, -window.WindowDays, @AsOfUtc),
        @AsOfUtc,
        N'Overall',
        N'All',
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        CONVERT(BIT, 0),
        N'SHADOW_ONLY',
        N'FIRST_APPROVED_PER_FIXTURE_CONFIGURATION'
    FROM @Windows AS window
    WHERE NOT EXISTS
    (
        SELECT 1 FROM @Scorecards AS scorecard
        WHERE scorecard.WindowDays = window.WindowDays
          AND scorecard.Dimension = N'Overall'
    );

    SELECT *
    FROM @Scorecards
    ORDER BY WindowDays, CASE Dimension WHEN N'Overall' THEN 0 WHEN N'Configuration' THEN 1
        WHEN N'MarketType' THEN 2 ELSE 3 END, Segment;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotH2026ShadowStatus
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DefinitionExists BIT = CASE WHEN EXISTS
        (SELECT 1 FROM dbo.AutomatedBotDefinitions WHERE BotKey = N'H2026') THEN 1 ELSE 0 END;
    DECLARE @IsEnabled BIT = COALESCE(
        (SELECT IsEnabled FROM dbo.AutomatedBotDefinitions WHERE BotKey = N'H2026'), 0);
    DECLARE @PublishEnabled BIT = COALESCE(
        (SELECT PublishEnabled FROM dbo.AutomatedBotDefinitions WHERE BotKey = N'H2026'), 1);
    DECLARE @CaptureTriggerEnabled BIT = CASE WHEN OBJECT_ID(
        N'dbo.trg_AutomatedBotPickEvaluations_CaptureH2026Shadow', N'TR') IS NOT NULL
        AND OBJECTPROPERTY(OBJECT_ID(N'dbo.trg_AutomatedBotPickEvaluations_CaptureH2026Shadow'), N'ExecIsTriggerDisabled') = 0
        THEN 1 ELSE 0 END;
    DECLARE @PublicationGuardsEnabled BIT = CASE WHEN
        OBJECT_ID(N'dbo.trg_AutomatedBotDefinitions_KeepH2026ShadowOnly', N'TR') IS NOT NULL
        AND OBJECTPROPERTY(OBJECT_ID(N'dbo.trg_AutomatedBotDefinitions_KeepH2026ShadowOnly'), N'ExecIsTriggerDisabled') = 0
        AND OBJECT_ID(N'dbo.trg_AutomatedCornerBetSelections_BlockH2026', N'TR') IS NOT NULL
        AND OBJECTPROPERTY(OBJECT_ID(N'dbo.trg_AutomatedCornerBetSelections_BlockH2026'), N'ExecIsTriggerDisabled') = 0
        THEN 1 ELSE 0 END;
    DECLARE @UnsafePublicationRows BIGINT =
        (SELECT COUNT_BIG(*) FROM dbo.AutomatedCornerBetSelections WHERE BotKey = N'H2026')
        +
        (SELECT COUNT_BIG(*) FROM dbo.AutomatedBotPickEvaluations
         WHERE BotKey = N'H2026'
           AND (PublishedSelectionId IS NOT NULL OR ISNULL(Published, 0) = 1 OR PublicationStatus = N'Published'));
    DECLARE @UncapturedEligibleEvaluations BIGINT =
        (SELECT COUNT_BIG(*)
         FROM dbo.AutomatedBotPickEvaluations AS evaluation
         WHERE evaluation.BotKey = N'H2026'
           AND evaluation.Decision IN (N'Approved', N'Rejected')
           AND evaluation.SelectedSide IN (N'Over', N'Under')
           AND evaluation.SelectedOdds > 1
           AND NOT EXISTS
           (
               SELECT 1 FROM dbo.BotH2026ShadowEvaluations AS shadow
               WHERE shadow.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId
           ));

    SELECT
        BotKey = N'H2026',
        SchemaReady = CONVERT(BIT, 1),
        DefinitionExists = @DefinitionExists,
        IsEnabled = @IsEnabled,
        PublishEnabled = @PublishEnabled,
        ShadowOnly = CONVERT(BIT, CASE WHEN @DefinitionExists = 1 AND @PublishEnabled = 0 THEN 1 ELSE 0 END),
        CaptureTriggerEnabled = @CaptureTriggerEnabled,
        PublicationGuardsEnabled = @PublicationGuardsEnabled,
        CapturedEvaluations = (SELECT COUNT_BIG(*) FROM dbo.BotH2026ShadowEvaluations),
        UnsafePublicationRows = @UnsafePublicationRows,
        UncapturedEligibleEvaluations = @UncapturedEligibleEvaluations,
        FirstPredictionTimestampUtc = (SELECT MIN(PredictionTimestampUtc) FROM dbo.BotH2026ShadowEvaluations),
        LastPredictionTimestampUtc = (SELECT MAX(PredictionTimestampUtc) FROM dbo.BotH2026ShadowEvaluations),
        State = CASE
            WHEN @DefinitionExists = 0 OR @PublishEnabled <> 0
                 OR @CaptureTriggerEnabled = 0 OR @PublicationGuardsEnabled = 0
                 OR @UnsafePublicationRows <> 0 THEN N'FAIL_CLOSED'
            WHEN @UncapturedEligibleEvaluations <> 0 THEN N'READY_WITH_LEGACY_GAPS'
            ELSE N'READY'
        END;
END;

GO
