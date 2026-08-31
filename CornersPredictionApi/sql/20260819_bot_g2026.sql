/*
    Bot G2026 - goals specialist / market-anchored probability.
    Additive and idempotent. Existing Bot A-F rows are not rewritten.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

GO

/* Schema and constraints must precede every G view/procedure. */
IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AutomatedBotDefinitions', N'PublishEnabled') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        ADD PublishEnabled BIT NOT NULL
            CONSTRAINT DF_AutomatedBotDefinitions_PublishEnabled DEFAULT (1) WITH VALUES;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.AutomatedBotDefinitions', N'LeagueFilterJson') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        ADD LeagueFilterJson NVARCHAR(MAX) NULL;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotDefinitions')
         AND name = N'CK_AutomatedBotDefinitions_BaseStrategy'
         AND definition NOT LIKE N'%GOALS_MARKET_ANCHORED%'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions
        DROP CONSTRAINT CK_AutomatedBotDefinitions_BaseStrategy;
END;

IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotDefinitions')
         AND name = N'CK_AutomatedBotDefinitions_BaseStrategy'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotDefinitions WITH CHECK
        ADD CONSTRAINT CK_AutomatedBotDefinitions_BaseStrategy CHECK
        (
            BaseStrategy IN
            (
                N'LEGACY_A', N'LEGACY_B', N'LEGACY_EMPIRICAL',
                N'MODELS_2026', N'GOALS_MARKET_ANCHORED'
            )
        );
END;

GO

-- Default pause requested for Chilean CORNERS. It is seeded once and remains
-- editable from the bot maintainer; all other market families stay eligible.
IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.AutomatedBotDefinitions
    SET LeagueFilterJson = N'[{"marketFamily":"CORNERS","includedLeagues":[],"excludedLeagues":["Chile - *"]}]',
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE BotKey IN (N'A', N'B', N'C2026', N'D2026', N'E2026', N'F2026')
      AND LeagueFilterJson IS NULL;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
BEGIN
    DECLARE @BotGColumns TABLE
    (
        Ordinal INT NOT NULL PRIMARY KEY,
        ColumnName SYSNAME NOT NULL,
        SqlDefinition NVARCHAR(4000) NOT NULL
    );

    INSERT INTO @BotGColumns(Ordinal, ColumnName, SqlDefinition)
    VALUES
        (1,  N'OddsSnapshotId', N'BIGINT NULL'),
        (2,  N'OddsTimestampUtc', N'DATETIME2(3) NULL'),
        (3,  N'PredictionTimestampUtc', N'DATETIME2(3) NULL'),
        (4,  N'Season', N'NVARCHAR(50) NULL'),
        (5,  N'Bookmaker', N'NVARCHAR(50) NULL'),
        (6,  N'MarketFamily', N'NVARCHAR(30) NULL'),
        (7,  N'OverOdds', N'DECIMAL(10,4) NULL'),
        (8,  N'UnderOdds', N'DECIMAL(10,4) NULL'),
        (9,  N'LegacyPrediction', N'DECIMAL(12,6) NULL'),
        (10, N'Prediction2026', N'DECIMAL(12,6) NULL'),
        (11, N'ContextPrediction', N'DECIMAL(12,6) NULL'),
        (12, N'HistoricalMean', N'DECIMAL(12,6) NULL'),
        (13, N'HistoricalMedian', N'DECIMAL(12,6) NULL'),
        (14, N'HistoricalStd', N'DECIMAL(12,6) NULL'),
        (15, N'PredictionMinusLine', N'DECIMAL(12,6) NULL'),
        (16, N'LegacyMinusMarketEquivalent', N'DECIMAL(12,6) NULL'),
        (17, N'Model2026MinusMarketEquivalent', N'DECIMAL(12,6) NULL'),
        (18, N'CandidateProbability', N'DECIMAL(9,6) NULL'),
        (19, N'CalibratedProbability', N'DECIMAL(9,6) NULL'),
        (20, N'ProbabilityLowerBound', N'DECIMAL(9,6) NULL'),
        (21, N'ProbabilityUpperBound', N'DECIMAL(9,6) NULL'),
        (22, N'ConservativeProbability', N'DECIMAL(9,6) NULL'),
        (23, N'RawEdge', N'DECIMAL(9,6) NULL'),
        (24, N'ConservativeEdge', N'DECIMAL(9,6) NULL'),
        (25, N'RawExpectedValue', N'DECIMAL(9,6) NULL'),
        (26, N'ConservativeExpectedValue', N'DECIMAL(9,6) NULL'),
        (27, N'UncertaintyScore', N'DECIMAL(9,6) NULL'),
        (28, N'CalibrationReliability', N'DECIMAL(9,6) NULL'),
        (29, N'OutOfDistributionScore', N'DECIMAL(9,6) NULL'),
        (30, N'MetaModelVersion', N'NVARCHAR(120) NULL'),
        (31, N'CalibrationVersion', N'NVARCHAR(120) NULL'),
        (32, N'UncertaintyVersion', N'NVARCHAR(120) NULL'),
        (33, N'OodVersion', N'NVARCHAR(120) NULL'),
        (34, N'Approved', N'BIT NULL'),
        (35, N'Published', N'BIT NULL'),
        (36, N'PublicationStatus', N'NVARCHAR(20) NULL'),
        (37, N'StakeUnits', N'DECIMAL(9,4) NULL'),
        (38, N'Result', N'NVARCHAR(20) NULL'),
        (39, N'ActualValue', N'DECIMAL(10,4) NULL'),
        (40, N'SettlementFactor', N'DECIMAL(9,4) NULL'),
        (41, N'ProfitLoss', N'DECIMAL(12,4) NULL'),
        (42, N'SettlementState', N'NVARCHAR(20) NULL'),
        (43, N'SettlementSource', N'NVARCHAR(80) NULL'),
        (44, N'SettlementSnapshotJson', N'NVARCHAR(MAX) NULL'),
        (45, N'OutcomeAvailableUtc', N'DATETIME2(3) NULL'),
        (46, N'SettledAtUtc', N'DATETIME2(3) NULL'),
        (47, N'OpeningLine', N'DECIMAL(6,2) NULL'),
        (48, N'OpeningOdds', N'DECIMAL(10,4) NULL'),
        (49, N'PublicationLine', N'DECIMAL(6,2) NULL'),
        (50, N'PublicationOdds', N'DECIMAL(10,4) NULL'),
        (51, N'ClosingLine', N'DECIMAL(6,2) NULL'),
        (52, N'ClosingOdds', N'DECIMAL(10,4) NULL'),
        (53, N'ClosingMarketNoVigProbability', N'DECIMAL(9,6) NULL'),
        (54, N'ClosingCapturedAtUtc', N'DATETIME2(3) NULL'),
        (55, N'CandidateUuid', N'UNIQUEIDENTIFIER NULL'),
        (56, N'ModelDisagreement', N'DECIMAL(9,6) NULL'),
        (57, N'GSelectionScore', N'DECIMAL(9,6) NULL'),
        (58, N'FixtureIdentity', N'BIGINT NULL');

    DECLARE @ColumnName SYSNAME;
    DECLARE @SqlDefinition NVARCHAR(4000);
    DECLARE @AlterSql NVARCHAR(MAX);
    DECLARE BotGColumnCursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT ColumnName, SqlDefinition FROM @BotGColumns ORDER BY Ordinal;

    OPEN BotGColumnCursor;
    FETCH NEXT FROM BotGColumnCursor INTO @ColumnName, @SqlDefinition;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF COL_LENGTH(N'dbo.AutomatedBotPickEvaluations', @ColumnName) IS NULL
        BEGIN
            SET @AlterSql = N'ALTER TABLE dbo.AutomatedBotPickEvaluations ADD ' +
                QUOTENAME(@ColumnName) + N' ' + @SqlDefinition + N';';
            EXEC sys.sp_executesql @AlterSql;
        END;
        FETCH NEXT FROM BotGColumnCursor INTO @ColumnName, @SqlDefinition;
    END;
    CLOSE BotGColumnCursor;
    DEALLOCATE BotGColumnCursor;
END;

GO

-- Compatibility only: pre-migration G rows used the single fixture field. New
-- writes keep canonical identity and verified API-Football identity separate.
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.AutomatedBotPickEvaluations
    SET FixtureIdentity = ApiFootballFixtureId
    WHERE BotKey = N'G2026'
      AND FixtureIdentity IS NULL
      AND ApiFootballFixtureId IS NOT NULL;
END;

GO

-- Snapshot-backed candidates may not have a mutable PartidosProximosCuotas row.
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1 FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'PartidoProximoCuotaId' AND is_nullable = 0
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations
        ALTER COLUMN PartidoProximoCuotaId BIGINT NULL;
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'CK_AutomatedBotPickEvaluations_Decision'
         AND definition NOT LIKE N'%Abstain%'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations
        DROP CONSTRAINT CK_AutomatedBotPickEvaluations_Decision;
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'CK_AutomatedBotPickEvaluations_Decision'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations WITH CHECK
        ADD CONSTRAINT CK_AutomatedBotPickEvaluations_Decision CHECK
        (Decision IN (N'Approved', N'Rejected', N'Abstain', N'PendingData', N'Invalid'));
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'UX_AutomatedBotPickEvaluations_G2026CandidateUuid'
   )
BEGIN
    CREATE UNIQUE INDEX UX_AutomatedBotPickEvaluations_G2026CandidateUuid
        ON dbo.AutomatedBotPickEvaluations(CandidateUuid)
        WHERE BotKey = N'G2026' AND CandidateUuid IS NOT NULL;
END;

-- Never hide legacy data corruption by deleting it during migration. Promotion
-- remains fail-closed until any pre-existing duplicate publication is audited.
IF EXISTS
   (
       SELECT FixtureIdentity
       FROM dbo.AutomatedBotPickEvaluations
       WHERE BotKey = N'G2026' AND Published = 1 AND FixtureIdentity IS NOT NULL
       GROUP BY FixtureIdentity
       HAVING COUNT_BIG(*) > 1
   )
    THROW 51030, 'Duplicate published Bot G fixture identities must be reconciled before migration.', 1;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'UX_AutomatedBotPickEvaluations_G2026PublishedFixture'
   )
BEGIN
    CREATE UNIQUE INDEX UX_AutomatedBotPickEvaluations_G2026PublishedFixture
        ON dbo.AutomatedBotPickEvaluations(FixtureIdentity)
        WHERE BotKey = N'G2026' AND Published = 1 AND FixtureIdentity IS NOT NULL;
END;

IF EXISTS
   (
       SELECT PublishedSelectionId
       FROM dbo.AutomatedBotPickEvaluations
       WHERE BotKey = N'G2026' AND PublishedSelectionId IS NOT NULL
       GROUP BY PublishedSelectionId
       HAVING COUNT_BIG(*) > 1
   )
    THROW 51031, 'Duplicate Bot G published-selection links must be reconciled before migration.', 1;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'UX_AutomatedBotPickEvaluations_G2026PublishedSelection'
   )
BEGIN
    CREATE UNIQUE INDEX UX_AutomatedBotPickEvaluations_G2026PublishedSelection
        ON dbo.AutomatedBotPickEvaluations(PublishedSelectionId)
        WHERE BotKey = N'G2026' AND PublishedSelectionId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'CK_AutomatedBotPickEvaluations_PublicationStatus'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations WITH CHECK
        ADD CONSTRAINT CK_AutomatedBotPickEvaluations_PublicationStatus CHECK
        (
            PublicationStatus IS NULL OR
            PublicationStatus IN (N'Shadow', N'Published', N'NotSelected')
        );
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.check_constraints
       WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'CK_AutomatedBotPickEvaluations_GResult'
   )
BEGIN
    ALTER TABLE dbo.AutomatedBotPickEvaluations WITH CHECK
        ADD CONSTRAINT CK_AutomatedBotPickEvaluations_GResult CHECK
        (
            Result IS NULL OR
            Result IN (N'Pending', N'Win', N'HalfWin', N'Push', N'HalfLoss', N'Loss', N'Void')
        );
END;

GO

/* Seed G only once. Re-running schema setup must not undo a later manual promotion. */
IF OBJECT_ID(N'dbo.AutomatedBotDefinitions', N'U') IS NOT NULL
BEGIN
    MERGE dbo.AutomatedBotDefinitions WITH (HOLDLOCK) AS target
    USING
    (
        SELECT
            N'G2026' AS BotKey,
            N'Bot G Goals Specialist' AS DisplayName,
            N'Especialista en goles anclado al mercado, con calibración, incertidumbre, OOD y abstención auditable.' AS Description,
            N'GOALS_MARKET_ANCHORED' AS BaseStrategy,
            CONVERT(BIT, 1) AS IsEnabled,
            CONVERT(BIT, 0) AS PublishEnabled,
            N'GOALS' AS MarketFamilies,
            CONVERT(DECIMAL(9,4), 1.0000) AS StakeMultiplier,
            N'{"botKey":"G2026","name":"Bot G Goals Specialist","baseStrategy":"GOALS_MARKET_ANCHORED","configurationVersion":"bot-g-goals-market-1.0.0","featureSchemaVersion":"bot-g-goals-features-1.0.0","legacyModelVersion":"goals_v1","model2026Version":"goals_deep_tuned_v2","enabled":true,"publishEnabled":false,"shadowMode":true,"stake":1.0,"supportedMarkets":["totalGoals","homeTeamGoals","awayTeamGoals"],"features":{"windows":[5,10,20],"decayFactor":0.85,"requiredVenueMatches":8,"minimumHistoricalMatches":8,"minimumStandardDeviation":0.25,"lineHistoryPriorStrength":20.0,"lineHitRatePriorMean":0.5,"pushRatePriorMean":0.08},"metaModel":{"required":true,"modelVersion":"bot-g-market-meta-1.0.0","featureSchemaVersion":"bot-g-goals-features-1.0.0","maximumAbsoluteResidualLogit":4.0},"calibration":{"version":"bot-g-calibration-1.0.0","method":"BetaCalibration","minimumEffectiveSampleSize":20,"outcomeAvailabilityLagHours":8,"globalPriorStrength":80.0,"marketPriorStrength":60.0,"selectionPriorStrength":40.0,"bookmakerPriorStrength":40.0},"uncertainty":{"version":"bot-g-uncertainty-1.0.0","confidenceZScore":1.645,"conservativeLambda":1.0,"minimumUncertainty":0.005,"maximumUncertainty":0.25,"useLowerBound":true},"outOfDistribution":{"version":"bot-g-ood-1.0.0","minimumReferenceSampleSize":30,"robustZScoreThreshold":3.5,"severeRobustZScore":8.0},"thresholds":{"minimumOdds":1.6,"maximumOdds":2.2,"minimumFinalProbability":0.54,"minimumConservativeEdge":0.02,"minimumConservativeExpectedValue":0.015,"minimumDataQuality":0.65,"minimumCalibrationReliability":0.3,"maximumProbabilityUncertainty":0.08,"maximumOodScore":0.7,"maximumModelDisagreement":1.5,"minimumHistoricalMatches":8,"minimumSettlementEffectiveSampleSize":40,"maximumOddsAgeMinutes":120},"ranking":{"conservativeExpectedValueWeight":0.35,"conservativeEdgeWeight":0.25,"calibrationReliabilityWeight":0.15,"dataQualityWeight":0.1,"inverseUncertaintyWeight":0.1,"contextAgreementWeight":0.05}}' AS StrategyConfigurationJson
    ) AS source
       ON target.BotKey = source.BotKey
    WHEN NOT MATCHED THEN INSERT
    (
        BotKey, DisplayName, Description, BaseStrategy, IsEnabled,
        PublishEnabled, IsBuiltIn, MarketFamilies, StakeMultiplier,
        StrategyConfigurationJson
    )
    VALUES
    (
        source.BotKey, source.DisplayName, source.Description,
        source.BaseStrategy, source.IsEnabled, source.PublishEnabled, 1,
        source.MarketFamilies, source.StakeMultiplier,
        source.StrategyConfigurationJson
    );
END;

GO

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026CandidateDate'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026CandidateDate
        ON dbo.AutomatedBotPickEvaluations(BotKey, PredictionTimestampUtc DESC)
        INCLUDE
        (
            FixtureIdentity, ApiFootballFixtureId, MarketType, SelectedSide, Bookmaker,
            ConfigurationVersion, Decision, PublicationStatus, Result
        )
        WHERE BotKey = N'G2026';
END;

IF OBJECT_ID(N'dbo.AutatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026FixtureMarket'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026FixtureMarket
        ON dbo.AutomatedBotPickEvaluations
        (
            BotKey, FixtureIdentity, MarketType, SelectedSide,
            LineValue, Bookmaker, ConfigurationVersion
        )
        INCLUDE
        (
            PredictionTimestampUtc, OddsTimestampUtc, SelectedOdds,
            ConservativeProbability, ConservativeEdge,
            ConservativeExpectedValue, Decision, Result
        )
        WHERE BotKey = N'G2026';
END;

-- The audit dashboard filters by fixture date, then orders by prediction time.
-- Keep this index narrow: the JSON snapshots are intentionally loaded only by
-- sp_GetBotG2026CandidateDetail, never by the paged list.
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026CandidateAuditV2'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026CandidateAuditV2
        ON dbo.AutomatedBotPickEvaluations
        (
            BotKey, MatchDate, PredictionTimestampUtc DESC,
            AutomatedBotPickEvaluationId DESC
        )
        INCLUDE
        (
            Decision, PublicationStatus, MarketType, SelectedSide,
            Bookmaker, Source, ConfigurationVersion, Result
        )
        WHERE BotKey = N'G2026';
END;

-- The date-first audit index keeps filtered counts cheap, while this second
-- narrow index serves the dashboard's stable newest-first page order without a
-- global sort. SQL Server can choose either index according to the date range.
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026CandidatePageV3'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026CandidatePageV3
        ON dbo.AutomatedBotPickEvaluations
        (
            BotKey, PredictionTimestampUtc DESC,
            AutomatedBotPickEvaluationId DESC
        )
        INCLUDE
        (
            MatchDate, Decision, PublicationStatus, MarketType,
            SelectedSide, Bookmaker, Source, ConfigurationVersion, Result
        )
        WHERE BotKey = N'G2026';
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026Scorecard'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026Scorecard
        ON dbo.AutomatedBotPickEvaluations
        (BotKey, ConfigurationVersion, MarketType, SelectedSide, Bookmaker, Result)
        INCLUDE
        (
            FixtureIdentity, ApiFootballFixtureId, PredictionTimestampUtc, Decision, Approved,
            Published, StakeUnits, SelectedOdds, MarketNoVigProbability,
            CalibratedProbability, ConservativeProbability, RawEdge,
            ConservativeEdge, RawExpectedValue, ConservativeExpectedValue,
            SettlementFactor, ProfitLoss, ClosingOdds
        )
        WHERE BotKey = N'G2026';
END;

-- Scorecards are requested by fixture-date windows.  The original scorecard
-- index started at ConfigurationVersion and therefore still scanned every G
-- candidate when the (usual) request omitted that optional filter.  Keep a
-- date-first, snapshot-free covering index for the online aggregation.
IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026ScorecardV2'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026ScorecardV2
        ON dbo.AutomatedBotPickEvaluations
        (BotKey, MatchDate, ConfigurationVersion)
        INCLUDE
        (
            AutomatedBotPickEvaluationId, FixtureIdentity, MarketType,
            SelectedSide, Bookmaker, Source, Decision, Published, Result,
            StakeUnits, ProfitLoss, SelectedOdds, RawEdge, ConservativeEdge,
            RawExpectedValue, ConservativeExpectedValue, UncertaintyScore,
            CalibrationReliability, OutOfDistributionScore,
            CalibratedProbability, MarketNoVigProbability,
            ClosingOdds, ClosingLine, LineValue
        )
        WHERE BotKey = N'G2026';
END;

IF OBJECT_ID(N'dbo.AutomatedBotPickEvaluations', N'U') IS NOT NULL
   AND NOT EXISTS
   (
       SELECT 1 FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
         AND name = N'IX_AutomatedBotPickEvaluations_G2026PendingSettlement'
   )
BEGIN
    CREATE INDEX IX_AutomatedBotPickEvaluations_G2026PendingSettlement
        ON dbo.AutomatedBotPickEvaluations(BotKey, Result, ApiFootballFixtureId)
        INCLUDE
        (
            PredictionTimestampUtc, MarketType, SelectedSide, LineValue,
            SelectedOdds, StakeUnits
        )
        WHERE BotKey = N'G2026' AND Result = N'Pending';
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedBotDefinitions
    @BotKeys NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        BotKey, DisplayName, Description, BaseStrategy, IsEnabled,
        PublishEnabled, IsBuiltIn, MarketFamilies, MinEdge, MinExpectedValue,
        MinDistanceToLine, MaxContextDifference, AllowModelDisagreement,
        MinOddsExclusive, MinProbabilityLiftOverImplied, StakeMultiplier,
        StrategyConfigurationJson, LeagueFilterJson, CreatedAtUtc, UpdatedAtUtc
    FROM dbo.AutomatedBotDefinitions
    WHERE @BotKeys IS NULL
       OR BotKey IN
          (
              SELECT UPPER(LTRIM(RTRIM(value)))
              FROM STRING_SPLIT(@BotKeys, N',')
          )
    ORDER BY IsBuiltIn DESC, BotKey;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedBotDefinition
    @BotKey NVARCHAR(50),
    @DisplayName NVARCHAR(120),
    @Description NVARCHAR(1000),
    @BaseStrategy NVARCHAR(30),
    @IsEnabled BIT,
    @MarketFamilies NVARCHAR(200),
    @MinEdge FLOAT = NULL,
    @MinExpectedValue FLOAT = NULL,
    @MinDistanceToLine FLOAT = NULL,
    @MaxContextDifference FLOAT = NULL,
    @AllowModelDisagreement BIT = NULL,
    @MinOddsExclusive FLOAT = NULL,
    @MinProbabilityLiftOverImplied FLOAT = NULL,
    @StakeMultiplier DECIMAL(9,4) = NULL,
    @StrategyConfigurationJson NVARCHAR(MAX) = NULL,
    @PublishEnabled BIT = NULL,
    @LeagueFilterJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @LeagueFilterJson IS NOT NULL AND ISJSON(@LeagueFilterJson) <> 1
        THROW 51063, 'LeagueFilterJson must be valid JSON.', 1;
    MERGE dbo.AutomatedBotDefinitions WITH (HOLDLOCK) AS target
    USING
    (
        SELECT
            UPPER(LTRIM(RTRIM(@BotKey))) AS BotKey,
            @DisplayName AS DisplayName,
            @Description AS Description,
            @BaseStrategy AS BaseStrategy,
            @IsEnabled AS IsEnabled,
            @PublishEnabled AS PublishEnabled,
            @MarketFamilies AS MarketFamilies,
            @MinEdge AS MinEdge,
            @MinExpectedValue AS MinExpectedValue,
            @MinDistanceToLine AS MinDistanceToLine,
            @MaxContextDifference AS MaxContextDifference,
            @AllowModelDisagreement AS AllowModelDisagreement,
            @MinOddsExclusive AS MinOddsExclusive,
            @MinProbabilityLiftOverImplied AS MinProbabilityLiftOverImplied,
            @StakeMultiplier AS StakeMultiplier,
            @StrategyConfigurationJson AS StrategyConfigurationJson,
            @LeagueFilterJson AS LeagueFilterJson
    ) AS source ON target.BotKey = source.BotKey
    WHEN MATCHED THEN UPDATE SET
        DisplayName = source.DisplayName,
        Description = source.Description,
        BaseStrategy = source.BaseStrategy,
        IsEnabled = source.IsEnabled,
        PublishEnabled = COALESCE(source.PublishEnabled, target.PublishEnabled),
        MarketFamilies = source.MarketFamilies,
        MinEdge = source.MinEdge,
        MinExpectedValue = source.MinExpectedValue,
        MinDistanceToLine = source.MinDistanceToLine,
        MaxContextDifference = source.MaxContextDifference,
        AllowModelDisagreement = source.AllowModelDisagreement,
        MinOddsExclusive = source.MinOddsExclusive,
        MinProbabilityLiftOverImplied = source.MinProbabilityLiftOverImplied,
        StakeMultiplier = source.StakeMultiplier,
        StrategyConfigurationJson = source.StrategyConfigurationJson,
        LeagueFilterJson = COALESCE(source.LeagueFilterJson, target.LeagueFilterJson),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT
    (
        BotKey, DisplayName, Description, BaseStrategy, IsEnabled,
        PublishEnabled, IsBuiltIn, MarketFamilies, MinEdge,
        MinExpectedValue, MinDistanceToLine, MaxContextDifference,
        AllowModelDisagreement, MinOddsExclusive,
        MinProbabilityLiftOverImplied, StakeMultiplier,
        StrategyConfigurationJson, LeagueFilterJson
    )
    VALUES
    (
        source.BotKey, source.DisplayName, source.Description,
        source.BaseStrategy, source.IsEnabled,
        COALESCE(source.PublishEnabled, CONVERT(BIT, 1)), 0,
        source.MarketFamilies, source.MinEdge, source.MinExpectedValue,
        source.MinDistanceToLine, source.MaxContextDifference,
        source.AllowModelDisagreement, source.MinOddsExclusive,
        source.MinProbabilityLiftOverImplied, source.StakeMultiplier,
        source.StrategyConfigurationJson, source.LeagueFilterJson
    );
    EXEC dbo.sp_GetAutomatedBotDefinitions @BotKeys = @BotKey;
END;

GO

CREATE OR ALTER VIEW dbo.vw_BotG2026Candidates
AS
    SELECT
        evaluation.AutomatedBotPickEvaluationId AS CandidateId,
        evaluation.IdempotencyKey,
        evaluation.CandidateUuid,
        evaluation.RunId,
        evaluation.BotKey,
        evaluation.AutomationVersion,
        evaluation.PartidoProximoCuotaId,
        evaluation.OddsSnapshotId,
        evaluation.OddsTimestampUtc,
        evaluation.FixtureIdentity AS FixtureId,
        evaluation.ApiFootballFixtureId AS OfficialFixtureId,
        evaluation.MatchDate AS FixtureDateUtc,
        evaluation.PredictionTimestampUtc,
        evaluation.League,
        evaluation.Season,
        evaluation.HomeTeam,
        evaluation.AwayTeam,
        COALESCE(evaluation.Bookmaker, evaluation.Source) AS Bookmaker,
        evaluation.Source,
        evaluation.SourceMarketType,
        COALESCE(evaluation.MarketFamily, N'GOALS') AS MarketFamily,
        evaluation.MarketType,
        evaluation.LineValue AS Line,
        evaluation.SelectedSide AS Selection,
        evaluation.OverOdds,
        evaluation.UnderOdds,
        evaluation.SelectedOdds,
        evaluation.RawImpliedProbability,
        evaluation.MarketNoVigProbability,
        evaluation.LegacyPrediction,
        evaluation.Prediction2026,
        evaluation.ContextPrediction,
        evaluation.HistoricalMean,
        evaluation.HistoricalMedian,
        evaluation.HistoricalStd,
        evaluation.PredictionMinusLine,
        evaluation.LegacyMinusMarketEquivalent,
        evaluation.Model2026MinusMarketEquivalent,
        evaluation.CandidateProbability,
        evaluation.CalibratedProbability,
        evaluation.FinalProbability,
        evaluation.ProbabilityLowerBound,
        evaluation.ProbabilityUpperBound,
        evaluation.ConservativeProbability,
        evaluation.RawEdge,
        evaluation.ConservativeEdge,
        evaluation.RawExpectedValue,
        evaluation.ConservativeExpectedValue,
        evaluation.DataQualityScore,
        evaluation.ContextAgreementScore,
        evaluation.UncertaintyScore,
        evaluation.CalibrationReliability,
        evaluation.OutOfDistributionScore,
        evaluation.ModelDisagreement,
        evaluation.GSelectionScore,
        evaluation.Decision,
        evaluation.DecisionReasonsJson,
        evaluation.RiskFlagsJson,
        evaluation.Explanation AS DecisionReason,
        evaluation.Approved,
        evaluation.Published,
        evaluation.PublicationStatus,
        evaluation.PublishedSelectionId,
        evaluation.ConfigurationVersion,
        evaluation.FeatureSchemaVersion,
        evaluation.BaseModelName,
        evaluation.BaseModelVersion,
        evaluation.BaseModelTrainedThroughUtc,
        evaluation.MetaModelVersion,
        evaluation.CalibrationVersion,
        evaluation.UncertaintyVersion,
        evaluation.OodVersion,
        evaluation.StakeUnits,
        evaluation.Result,
        evaluation.ActualValue,
        evaluation.SettlementFactor,
        evaluation.ProfitLoss,
        evaluation.SettlementState,
        evaluation.SettlementSource,
        evaluation.SettlementSnapshotJson,
        evaluation.OutcomeAvailableUtc,
        evaluation.SettledAtUtc,
        evaluation.OpeningLine,
        evaluation.OpeningOdds,
        evaluation.PublicationLine,
        evaluation.PublicationOdds,
        evaluation.ClosingLine,
        evaluation.ClosingOdds,
        evaluation.ClosingMarketNoVigProbability,
        evaluation.ClosingCapturedAtUtc,
        evaluation.FeatureSnapshotJson,
        evaluation.EvaluatedAtUtc,
        evaluation.UpdatedAtUtc
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'G2026';

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertBotG2026Candidate
    @IdempotencyKey CHAR(64),
    @RunId UNIQUEIDENTIFIER,
    @CandidateUuid UNIQUEIDENTIFIER,
    @AutomationVersion NVARCHAR(50),
    @PartidoProximoCuotaId BIGINT = NULL,
    @OddsSnapshotId BIGINT = NULL,
    @OddsTimestampUtc DATETIME2(3),
    @FixtureId BIGINT,
    @ApiFootballFixtureId BIGINT = NULL,
    @FixtureDateUtc DATETIME2(0),
    @PredictionTimestampUtc DATETIME2(3),
    @League NVARCHAR(200),
    @Season NVARCHAR(50) = NULL,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @Bookmaker NVARCHAR(50),
    @SourceMarketType NVARCHAR(50),
    @MarketType NVARCHAR(50),
    @Line DECIMAL(6,2),
    @Selection NVARCHAR(10),
    @OverOdds DECIMAL(10,4),
    @UnderOdds DECIMAL(10,4),
    @SelectedOdds DECIMAL(10,4),
    @Decision NVARCHAR(20),
    @DecisionReason NVARCHAR(1000),
    @DecisionReasonsJson NVARCHAR(MAX),
    @RiskFlagsJson NVARCHAR(MAX),
    @FeatureSnapshotJson NVARCHAR(MAX),
    @ConfigurationVersion NVARCHAR(80),
    @FeatureSchemaVersion NVARCHAR(80),
    @BaseModelName NVARCHAR(120) = NULL,
    @BaseModelVersion NVARCHAR(120) = NULL,
    @BaseModelTrainedThroughUtc DATETIME2(0) = NULL,
    @MetaModelVersion NVARCHAR(120) = NULL,
    @CalibrationVersion NVARCHAR(120) = NULL,
    @UncertaintyVersion NVARCHAR(120) = NULL,
    @OodVersion NVARCHAR(120) = NULL,
    @RawImpliedProbability DECIMAL(9,6) = NULL,
    @MarketNoVigProbability DECIMAL(9,6) = NULL,
    @LegacyPrediction DECIMAL(12,6) = NULL,
    @Prediction2026 DECIMAL(12,6) = NULL,
    @ContextPrediction DECIMAL(12,6) = NULL,
    @HistoricalMean DECIMAL(12,6) = NULL,
    @HistoricalMedian DECIMAL(12,6) = NULL,
    @HistoricalStd DECIMAL(12,6) = NULL,
    @PredictionMinusLine DECIMAL(12,6) = NULL,
    @LegacyMinusMarketEquivalent DECIMAL(12,6) = NULL,
    @Model2026MinusMarketEquivalent DECIMAL(12,6) = NULL,
    @CandidateProbability DECIMAL(9,6) = NULL,
    @CalibratedProbability DECIMAL(9,6) = NULL,
    @FinalProbability DECIMAL(9,6) = NULL,
    @ProbabilityLowerBound DECIMAL(9,6) = NULL,
    @ProbabilityUpperBound DECIMAL(9,6) = NULL,
    @ConservativeProbability DECIMAL(9,6) = NULL,
    @RawEdge DECIMAL(9,6) = NULL,
    @ConservativeEdge DECIMAL(9,6) = NULL,
    @RawExpectedValue DECIMAL(9,6) = NULL,
    @ConservativeExpectedValue DECIMAL(9,6) = NULL,
    @DataQualityScore DECIMAL(9,6) = NULL,
    @ContextAgreementScore DECIMAL(9,6) = NULL,
    @UncertaintyScore DECIMAL(9,6) = NULL,
    @CalibrationReliability DECIMAL(9,6) = NULL,
    @OutOfDistributionScore DECIMAL(9,6) = NULL,
    @ModelDisagreement DECIMAL(9,6) = NULL,
    @GSelectionScore DECIMAL(9,6) = NULL,
    @Published BIT = 0,
    @PublicationStatus NVARCHAR(20) = N'Shadow',
    @PublishedSelectionId BIGINT = NULL,
    @StakeUnits DECIMAL(9,4) = 1.0000
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @RunId = CONVERT(UNIQUEIDENTIFIER, N'00000000-0000-0000-0000-000000000000')
        THROW 50999, 'Bot G2026 requires a non-empty run id.', 1;
    IF RIGHT(UPPER(LTRIM(RTRIM(@AutomationVersion))), 6) <> N'-G2026'
        THROW 50998, 'Bot G2026 automation version must end with -G2026.', 1;
    IF @FixtureId <= 0
        THROW 51000, 'Bot G2026 requires a positive, auditable fixture id.', 1;
    IF @MarketType NOT IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
        THROW 51001, 'Bot G2026 only supports goals markets.', 1;
    IF @Selection NOT IN (N'Over', N'Under')
        THROW 51002, 'Bot G2026 selection must be Over or Under.', 1;
    IF @Decision NOT IN (N'Approved', N'Rejected', N'Abstain')
        THROW 51003, 'Bot G2026 decision must be Approved, Rejected or Abstain.', 1;
    IF @PublicationStatus NOT IN (N'Shadow', N'Published', N'NotSelected')
        THROW 51004, 'Invalid Bot G2026 publication status.', 1;
    IF @OddsTimestampUtc > @PredictionTimestampUtc
        THROW 51005, 'Bot G2026 odds must exist at or before prediction time.', 1;
    IF @PredictionTimestampUtc >= @FixtureDateUtc
        THROW 51006, 'Bot G2026 prediction timestamp must precede kickoff.', 1;
    IF @Published = 1 AND (@PublishedSelectionId IS NULL OR @PublicationStatus <> N'Published')
        THROW 51007, 'Published candidates require a published selection id and status.', 1;
    IF @Published = 1 AND NOT EXISTS
       (
           SELECT 1 FROM dbo.AutomatedBotDefinitions
           WHERE BotKey = N'G2026' AND IsEnabled = 1 AND PublishEnabled = 1
       )
        THROW 51008, 'Bot G2026 publication is disabled; candidate must remain shadow.', 1;
    IF EXISTS
       (
           SELECT 1 FROM dbo.AutomatedBotPickEvaluations
           WHERE IdempotencyKey = @IdempotencyKey AND BotKey <> N'G2026'
       )
        THROW 51009, 'Candidate idempotency key collides with another bot.', 1;
    IF EXISTS
       (
           SELECT 1
           FROM dbo.AutomatedBotPickEvaluations
           WHERE IdempotencyKey = @IdempotencyKey
             AND BotKey = N'G2026'
             AND
             (
                 FixtureIdentity <> @FixtureId
                 OR
                 (ApiFootballFixtureId IS NOT NULL
                  AND @ApiFootballFixtureId IS NOT NULL
                  AND ApiFootballFixtureId <> @ApiFootballFixtureId)
             )
       )
        THROW 51010, 'Candidate idempotency key conflicts with an existing fixture identity.', 1;

    -- Metrics are immutable for an idempotency key. A retry may only attach
    -- publication metadata; changing a model/threshold requires a new version.
    MERGE dbo.AutomatedBotPickEvaluations WITH (HOLDLOCK) AS target
    USING (SELECT @IdempotencyKey AS IdempotencyKey) AS source
       ON target.IdempotencyKey = source.IdempotencyKey
    WHEN MATCHED AND target.BotKey = N'G2026' THEN UPDATE SET
        ApiFootballFixtureId = COALESCE(target.ApiFootballFixtureId, @ApiFootballFixtureId),
        Published = IIF(target.Published = 1, 1, @Published),
        PublicationStatus = CASE WHEN target.Published = 1
            THEN N'Published' ELSE @PublicationStatus END,
        PublishedSelectionId = COALESCE(target.PublishedSelectionId, @PublishedSelectionId),
        PublicationLine = CASE WHEN @Published = 1 THEN @Line ELSE target.PublicationLine END,
        PublicationOdds = CASE WHEN @Published = 1 THEN @SelectedOdds ELSE target.PublicationOdds END,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT
    (
        IdempotencyKey, CandidateUuid, RunId, BotKey, AutomationVersion,
        PartidoProximoCuotaId, OddsSnapshotId, OddsTimestampUtc,
        FixtureIdentity, ApiFootballFixtureId, MatchDate, PredictionTimestampUtc, League,
        Season, HomeTeam, AwayTeam, Source, Bookmaker, SourceMarketType,
        MarketFamily, MarketType, LineValue, SelectedSide, OverOdds,
        UnderOdds, SelectedOdds, DecisionEngineType, Decision, BaseModelName,
        BaseModelVersion, BaseModelTrainedThroughUtc, FeatureSchemaVersion,
        ConfigurationVersion, MetaModelVersion, CalibrationVersion,
        UncertaintyVersion, OodVersion, BaseRawProbability,
        BaseCalibratedProbability, RawImpliedProbability,
        MarketNoVigProbability, FinalProbability, FinalEdge,
        FinalExpectedValue, LegacyPrediction, Prediction2026,
        ContextPrediction, HistoricalMean, HistoricalMedian, HistoricalStd,
        PredictionMinusLine, LegacyMinusMarketEquivalent,
        Model2026MinusMarketEquivalent, CandidateProbability,
        CalibratedProbability, ProbabilityLowerBound, ProbabilityUpperBound,
        ConservativeProbability, RawEdge, ConservativeEdge,
        RawExpectedValue, ConservativeExpectedValue, DataQualityScore,
        ContextAgreementScore, UncertaintyScore, CalibrationReliability,
        OutOfDistributionScore, ModelDisagreement, GSelectionScore,
        DecisionReasonsJson, RiskFlagsJson,
        Explanation, FeatureSnapshotJson, Approved, Published,
        PublicationStatus, PublishedSelectionId, StakeUnits, Result,
        SettlementState, OpeningLine, OpeningOdds
    )
    VALUES
    (
        @IdempotencyKey, @CandidateUuid, @RunId, N'G2026', @AutomationVersion,
        @PartidoProximoCuotaId, @OddsSnapshotId, @OddsTimestampUtc,
        @FixtureId, @ApiFootballFixtureId, @FixtureDateUtc, @PredictionTimestampUtc, @League,
        @Season, @HomeTeam, @AwayTeam, @Bookmaker, @Bookmaker,
        @SourceMarketType, N'GOALS', @MarketType, @Line, @Selection,
        @OverOdds, @UnderOdds, @SelectedOdds, N'GoalsMarketAnchored',
        @Decision, @BaseModelName, @BaseModelVersion,
        @BaseModelTrainedThroughUtc, @FeatureSchemaVersion,
        @ConfigurationVersion, @MetaModelVersion, @CalibrationVersion,
        @UncertaintyVersion, @OodVersion, @CandidateProbability,
        @CalibratedProbability, @RawImpliedProbability,
        @MarketNoVigProbability, COALESCE(@FinalProbability, @CalibratedProbability),
        @RawEdge, @RawExpectedValue, @LegacyPrediction, @Prediction2026,
        @ContextPrediction, @HistoricalMean, @HistoricalMedian,
        @HistoricalStd, @PredictionMinusLine,
        @LegacyMinusMarketEquivalent, @Model2026MinusMarketEquivalent,
        @CandidateProbability, @CalibratedProbability,
        @ProbabilityLowerBound, @ProbabilityUpperBound,
        @ConservativeProbability, @RawEdge, @ConservativeEdge,
        @RawExpectedValue, @ConservativeExpectedValue, @DataQualityScore,
        @ContextAgreementScore, @UncertaintyScore,
        @CalibrationReliability, @OutOfDistributionScore,
        @ModelDisagreement, @GSelectionScore,
        COALESCE(@DecisionReasonsJson, N'[]'),
        COALESCE(@RiskFlagsJson, N'[]'), @DecisionReason,
        COALESCE(@FeatureSnapshotJson, N'{}'),
        IIF(@Decision = N'Approved', 1, 0), @Published,
        @PublicationStatus, @PublishedSelectionId, @StakeUnits,
        N'Pending', N'Pending', @Line, @SelectedOdds
    );

    SELECT CandidateId
    FROM dbo.vw_BotG2026Candidates
    WHERE IdempotencyKey = @IdempotencyKey;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026Candidates
    @DateFromUtc DATETIME2(0) = NULL,
    @DateToUtc DATETIME2(0) = NULL,
    @Decision NVARCHAR(20) = NULL,
    @PublicationStatus NVARCHAR(20) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @Selection NVARCHAR(10) = NULL,
    @Bookmaker NVARCHAR(50) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL,
    @Result NVARCHAR(20) = NULL,
    @Page INT = 1,
    @PageSize INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 SET @PageSize = 1;
    IF @PageSize > 1000 SET @PageSize = 1000;

    DECLARE @TotalRows BIGINT;
    DECLARE @Offset BIGINT = CONVERT(BIGINT, @Page - 1) * @PageSize;

    -- Count over the narrow audit index. OPTION(RECOMPILE) lets SQL Server
    -- remove unused optional predicates instead of reusing a poor sniffed plan.
    SELECT @TotalRows = COUNT_BIG(*)
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'G2026'
      AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
      AND (@DateToUtc IS NULL OR evaluation.MatchDate < @DateToUtc)
      AND (@Decision IS NULL OR evaluation.Decision = @Decision)
      AND (@PublicationStatus IS NULL OR evaluation.PublicationStatus = @PublicationStatus)
      AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
      AND (@Selection IS NULL OR evaluation.SelectedSide = @Selection)
      AND
      (
          @Bookmaker IS NULL
          OR evaluation.Bookmaker = @Bookmaker
          OR (evaluation.Bookmaker IS NULL AND evaluation.Source = @Bookmaker)
      )
      AND (@ConfigurationVersion IS NULL OR evaluation.ConfigurationVersion = @ConfigurationVersion)
      AND (@Result IS NULL OR evaluation.Result = @Result)
    OPTION (RECOMPILE);

    CREATE TABLE #CandidatePage
    (
        CandidateId BIGINT NOT NULL PRIMARY KEY,
        PredictionTimestampUtc DATETIME2(3) NULL
    );

    -- Materialize only the requested keys before touching any wide columns.
    INSERT INTO #CandidatePage(CandidateId, PredictionTimestampUtc)
    SELECT
        evaluation.AutomatedBotPickEvaluationId,
        evaluation.PredictionTimestampUtc
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'G2026'
      AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
      AND (@DateToUtc IS NULL OR evaluation.MatchDate < @DateToUtc)
      AND (@Decision IS NULL OR evaluation.Decision = @Decision)
      AND (@PublicationStatus IS NULL OR evaluation.PublicationStatus = @PublicationStatus)
      AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
      AND (@Selection IS NULL OR evaluation.SelectedSide = @Selection)
      AND
      (
          @Bookmaker IS NULL
          OR evaluation.Bookmaker = @Bookmaker
          OR (evaluation.Bookmaker IS NULL AND evaluation.Source = @Bookmaker)
      )
      AND (@ConfigurationVersion IS NULL OR evaluation.ConfigurationVersion = @ConfigurationVersion)
      AND (@Result IS NULL OR evaluation.Result = @Result)
    ORDER BY evaluation.PredictionTimestampUtc DESC,
             evaluation.AutomatedBotPickEvaluationId DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);

    -- FeatureSnapshotJson and settlement snapshots are deliberately excluded
    -- from the list. The candidate-detail endpoint still returns them in full.
    SELECT
        candidate.CandidateId,
        candidate.CandidateUuid,
        candidate.RunId,
        candidate.BotKey,
        candidate.AutomationVersion,
        candidate.OddsSnapshotId AS SourceOddsId,
        candidate.OddsSnapshotId,
        candidate.OddsTimestampUtc,
        candidate.FixtureId,
        candidate.OfficialFixtureId,
        candidate.FixtureDateUtc,
        candidate.PredictionTimestampUtc,
        candidate.League,
        candidate.Season,
        candidate.HomeTeam,
        candidate.AwayTeam,
        candidate.Bookmaker,
        candidate.SourceMarketType,
        candidate.MarketFamily,
        candidate.MarketType,
        candidate.Line,
        candidate.Selection,
        candidate.OverOdds,
        candidate.UnderOdds,
        candidate.SelectedOdds,
        candidate.RawImpliedProbability,
        candidate.MarketNoVigProbability,
        candidate.LegacyPrediction,
        candidate.Prediction2026,
        candidate.ContextPrediction,
        candidate.HistoricalMean,
        candidate.HistoricalMedian,
        candidate.HistoricalStd,
        candidate.PredictionMinusLine,
        candidate.LegacyMinusMarketEquivalent,
        candidate.Model2026MinusMarketEquivalent,
        candidate.CandidateProbability,
        candidate.CalibratedProbability,
        candidate.FinalProbability,
        candidate.ProbabilityLowerBound,
        candidate.ProbabilityUpperBound,
        candidate.ConservativeProbability,
        candidate.RawEdge,
        candidate.ConservativeEdge,
        candidate.RawExpectedValue,
        candidate.ConservativeExpectedValue,
        candidate.DataQualityScore,
        candidate.ContextAgreementScore,
        candidate.UncertaintyScore,
        candidate.CalibrationReliability,
        candidate.OutOfDistributionScore,
        candidate.ModelDisagreement,
        candidate.GSelectionScore,
        candidate.Decision,
        candidate.DecisionReason,
        -- Large JSON is fetched only through sp_GetBotG2026CandidateDetail when
        -- the user opens one audit. Keeping it out of every page cuts the first
        -- 100-row response by roughly 180 KB on the current data set.
        N'[]' AS DecisionReasonsJson,
        N'[]' AS RiskFlagsJson,
        candidate.Approved,
        candidate.Published,
        candidate.PublicationStatus,
        candidate.PublishedSelectionId,
        candidate.ConfigurationVersion,
        candidate.FeatureSchemaVersion,
        candidate.BaseModelVersion,
        candidate.BaseModelTrainedThroughUtc,
        candidate.MetaModelVersion,
        candidate.CalibrationVersion,
        candidate.UncertaintyVersion,
        candidate.OodVersion,
        candidate.StakeUnits,
        candidate.Result,
        candidate.ActualValue,
        candidate.SettlementFactor,
        candidate.ProfitLoss,
        candidate.SettlementState,
        candidate.OutcomeAvailableUtc,
        candidate.SettledAtUtc,
        candidate.ClosingLine,
        candidate.ClosingOdds,
        candidate.ClosingMarketNoVigProbability,
        candidate.ClosingCapturedAtUtc,
        N'{}' AS FeatureSnapshotJson,
        candidate.EvaluatedAtUtc,
        candidate.UpdatedAtUtc,
        @TotalRows AS TotalRows
    FROM #CandidatePage AS page
    INNER JOIN dbo.vw_BotG2026Candidates AS candidate
        ON candidate.CandidateId = page.CandidateId
    ORDER BY page.PredictionTimestampUtc DESC, page.CandidateId DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026CandidateDetail
    @CandidateId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vw_BotG2026Candidates
    WHERE CandidateId = @CandidateId;
END;

GO

/*
    Returns only snapshots known at prediction time. Captures at kickoff or
    later are excluded and therefore cannot leak closing odds into features.
*/
CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026OddsAsOf
    @PredictionTimestampUtc DATETIME2(3),
    @MatchDateFromUtc DATETIME2(0) = NULL,
    @MatchDateToUtc DATETIME2(0) = NULL,
    @Source NVARCHAR(50) = NULL,
    @SourceMatchId NVARCHAR(100) = NULL,
    @MaximumRows INT = 5000
AS
BEGIN
    SET NOCOUNT ON;
    IF @MaximumRows < 1 SET @MaximumRows = 1;
    IF @MaximumRows > 50000 SET @MaximumRows = 50000;

    ;WITH RankedSnapshots AS
    (
        SELECT
            snapshot.CornerOddsSnapshotId AS OddsSnapshotId,
            snapshot.CapturedAtUtc AS OddsTimestampUtc,
            snapshot.Source,
            snapshot.SourceMatchId,
            snapshot.SourceUrl,
            snapshot.MatchDate AS FixtureDateUtc,
            snapshot.League,
            snapshot.StandardizedLeague,
            snapshot.HomeTeam,
            snapshot.AwayTeam,
            snapshot.StandardizedHomeTeam,
            snapshot.StandardizedAwayTeam,
            snapshot.MarketType AS SourceMarketType,
            CASE snapshot.MarketType
                WHEN N'GoalsTotal' THEN N'TotalGoals'
                WHEN N'GoalsHomeTeam' THEN N'HomeTeamGoals'
                WHEN N'GoalsAwayTeam' THEN N'AwayTeamGoals'
            END AS MarketType,
            snapshot.LineValue AS Line,
            snapshot.OverOdds,
            snapshot.UnderOdds,
            ROW_NUMBER() OVER
            (
                PARTITION BY
                    snapshot.Source,
                    COALESCE(snapshot.SourceMatchId, N''),
                    snapshot.MatchDate,
                    COALESCE(snapshot.StandardizedHomeTeam, snapshot.HomeTeam),
                    COALESCE(snapshot.StandardizedAwayTeam, snapshot.AwayTeam),
                    snapshot.MarketType,
                    snapshot.LineValue
                ORDER BY snapshot.CapturedAtUtc DESC, snapshot.CornerOddsSnapshotId DESC
            ) AS SnapshotRank
        FROM dbo.CornerOddsSnapshots AS snapshot
        WHERE snapshot.CapturedAtUtc <= @PredictionTimestampUtc
          AND snapshot.MatchDate > @PredictionTimestampUtc
          AND snapshot.MarketType IN (N'GoalsTotal', N'GoalsHomeTeam', N'GoalsAwayTeam')
          AND (@MatchDateFromUtc IS NULL OR snapshot.MatchDate >= @MatchDateFromUtc)
          AND (@MatchDateToUtc IS NULL OR snapshot.MatchDate < @MatchDateToUtc)
          AND (@Source IS NULL OR snapshot.Source = @Source)
          AND (@SourceMatchId IS NULL OR snapshot.SourceMatchId = @SourceMatchId)
    )
    SELECT TOP (@MaximumRows)
        OddsSnapshotId, OddsTimestampUtc, Source, SourceMatchId, SourceUrl,
        FixtureDateUtc, League, StandardizedLeague, HomeTeam, AwayTeam,
        StandardizedHomeTeam, StandardizedAwayTeam, SourceMarketType, MarketType, Line,
        OverOdds, UnderOdds
    FROM RankedSnapshots
    WHERE SnapshotRank = 1
    ORDER BY FixtureDateUtc, Source, HomeTeam, AwayTeam, MarketType, Line;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026TrainingExport
    @AsOfUtc DATETIME2(3),
    @DateFromUtc DATETIME2(0) = NULL,
    @DateToUtc DATETIME2(0) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL,
    @OnlyOutcomeAvailable BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        candidate.CandidateId,
        QuoteId = CONCAT
        (
            candidate.FixtureId, N'|', candidate.Bookmaker, N'|',
            candidate.MarketType, N'|', CONVERT(NVARCHAR(20), candidate.Line), N'|',
            candidate.OddsSnapshotId
        ),
        candidate.FixtureId,
        candidate.FixtureDateUtc,
        candidate.PredictionTimestampUtc,
        lineage.FeatureAsOfUtc,
        OddsTimestampUtc = linkedSnapshot.CapturedAtUtc,
        candidate.OutcomeAvailableUtc,
        candidate.League,
        candidate.Season,
        candidate.HomeTeam,
        candidate.AwayTeam,
        candidate.Bookmaker,
        candidate.MarketType,
        candidate.Selection,
        candidate.Line,
        OverOdds = linkedSnapshot.OverOdds,
        UnderOdds = linkedSnapshot.UnderOdds,
        SelectedOdds = CASE candidate.Selection
            WHEN N'Over' THEN linkedSnapshot.OverOdds
            ELSE linkedSnapshot.UnderOdds
        END,
        candidate.LegacyPrediction,
        lineage.LegacyModelVersion,
        lineage.LegacyModelTrainedThroughUtc,
        candidate.Prediction2026,
        lineage.Model2026Version,
        lineage.Model2026TrainedThroughUtc,
        candidate.ContextPrediction,
        candidate.HistoricalMean,
        candidate.HistoricalStd,
        lineage.HistoryCount,
        candidate.DataQualityScore,
        candidate.ActualValue,
        candidate.ConfigurationVersion,
        candidate.FeatureSchemaVersion,
        candidate.Decision,
        candidate.Result,
        candidate.SettlementFactor,
        candidate.ProfitLoss,
        FPublished = CONVERT(BIT, IIF(fCandidate.PublishedSelectionId IS NULL, 0, 1)),
        FProbability = fCandidate.FinalProbability,
        FEdge = fCandidate.FinalEdge,
        FExpectedValue = fCandidate.FinalExpectedValue,
        LineageJson = JSON_QUERY(featureDocument.SafeJson, N'$.lineage'),
        candidate.FeatureSnapshotJson,
        IsSynthetic = CONVERT(BIT, 0)
    FROM dbo.vw_BotG2026Candidates AS candidate
    INNER JOIN dbo.CornerOddsSnapshots AS linkedSnapshot
      ON linkedSnapshot.CornerOddsSnapshotId = candidate.OddsSnapshotId
     AND linkedSnapshot.CapturedAtUtc <= candidate.PredictionTimestampUtc
    CROSS APPLY
    (
        SELECT SafeJson = CASE
            WHEN ISJSON(candidate.FeatureSnapshotJson) = 1
                THEN candidate.FeatureSnapshotJson
            ELSE N'{}'
        END
    ) AS featureDocument
    CROSS APPLY
    (
        SELECT
            FeatureAsOfUtc = COALESCE
            (
                CONVERT
                (
                    DATETIME2(3),
                    TRY_CONVERT
                    (
                        DATETIMEOFFSET(3),
                        JSON_VALUE(featureDocument.SafeJson, N'$.features.asOfDateUtc'),
                        127
                    )
                ),
                candidate.PredictionTimestampUtc
            ),
            LegacyModelVersion = NULLIF
            (
                JSON_VALUE(featureDocument.SafeJson, N'$.lineage.legacyModelVersion'),
                N''
            ),
            LegacyModelTrainedThroughUtc = CONVERT
            (
                DATETIME2(3),
                TRY_CONVERT
                (
                    DATETIMEOFFSET(3),
                    JSON_VALUE(featureDocument.SafeJson, N'$.lineage.legacyTrainedThroughUtc'),
                    127
                )
            ),
            Model2026Version = NULLIF
            (
                JSON_VALUE(featureDocument.SafeJson, N'$.lineage.model2026Version'),
                N''
            ),
            Model2026TrainedThroughUtc = CONVERT
            (
                DATETIME2(3),
                TRY_CONVERT
                (
                    DATETIMEOFFSET(3),
                    JSON_VALUE(featureDocument.SafeJson, N'$.lineage.model2026TrainedThroughUtc'),
                    127
                )
            ),
            HistoryCount = TRY_CONVERT
            (
                INT,
                JSON_VALUE(featureDocument.SafeJson, N'$.features.historyCount')
            )
    ) AS lineage
    OUTER APPLY
    (
        SELECT TOP (1)
            f.PublishedSelectionId,
            f.FinalProbability,
            f.FinalEdge,
            f.FinalExpectedValue
        FROM dbo.AutomatedBotPickEvaluations AS f
        WHERE f.BotKey = N'F2026'
          AND candidate.OfficialFixtureId IS NOT NULL
          AND f.ApiFootballFixtureId = candidate.OfficialFixtureId
          AND COALESCE(f.Bookmaker, f.Source) = candidate.Bookmaker
          AND f.MarketType = candidate.MarketType
          AND f.SelectedSide = candidate.Selection
          AND f.LineValue = candidate.Line
          AND COALESCE(f.PredictionTimestampUtc, f.EvaluatedAtUtc) <= candidate.PredictionTimestampUtc
        ORDER BY
            IIF(f.PublishedSelectionId IS NULL, 0, 1) DESC,
            COALESCE(f.PredictionTimestampUtc, f.EvaluatedAtUtc) DESC,
            f.AutomatedBotPickEvaluationId DESC
    ) AS fCandidate
    WHERE candidate.PredictionTimestampUtc <= @AsOfUtc
      AND linkedSnapshot.CapturedAtUtc = candidate.OddsTimestampUtc
      AND linkedSnapshot.OverOdds > 1
      AND linkedSnapshot.UnderOdds > 1
      AND lineage.FeatureAsOfUtc <= candidate.PredictionTimestampUtc
      AND candidate.PredictionTimestampUtc < candidate.FixtureDateUtc
      AND lineage.LegacyModelVersion IS NOT NULL
      AND lineage.LegacyModelTrainedThroughUtc < candidate.PredictionTimestampUtc
      AND lineage.Model2026Version IS NOT NULL
      AND lineage.Model2026TrainedThroughUtc < candidate.PredictionTimestampUtc
      AND lineage.HistoryCount IS NOT NULL
      AND candidate.LegacyPrediction IS NOT NULL
      AND candidate.Prediction2026 IS NOT NULL
      AND candidate.ContextPrediction IS NOT NULL
      AND candidate.HistoricalMean IS NOT NULL
      AND candidate.HistoricalStd IS NOT NULL
      AND candidate.DataQualityScore IS NOT NULL
      AND (@DateFromUtc IS NULL OR candidate.FixtureDateUtc >= @DateFromUtc)
      AND (@DateToUtc IS NULL OR candidate.FixtureDateUtc < @DateToUtc)
      AND (@ConfigurationVersion IS NULL OR candidate.ConfigurationVersion = @ConfigurationVersion)
      AND
      (
          @OnlyOutcomeAvailable = 0 OR
          (
              candidate.OutcomeAvailableUtc IS NOT NULL AND
              candidate.OutcomeAvailableUtc <= @AsOfUtc AND
              candidate.Result IN (N'Win', N'HalfWin', N'Push', N'HalfLoss', N'Loss')
          )
      )
    ORDER BY candidate.PredictionTimestampUtc, candidate.CandidateId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_SettleBotG2026Candidate
    @CandidateId BIGINT,
    @Result NVARCHAR(20),
    @ActualValue DECIMAL(10,4) = NULL,
    @SettlementFactor DECIMAL(9,4) = NULL,
    @ProfitLoss DECIMAL(12,4) = NULL,
    @SettlementSource NVARCHAR(80) = NULL,
    @SettlementSnapshotJson NVARCHAR(MAX) = NULL,
    @OutcomeAvailableUtc DATETIME2(3),
    @SettledAtUtc DATETIME2(3),
    @ClosingLine DECIMAL(6,2) = NULL,
    @ClosingOdds DECIMAL(10,4) = NULL,
    @ClosingMarketNoVigProbability DECIMAL(9,6) = NULL,
    @ClosingCapturedAtUtc DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Result NOT IN (N'Win', N'HalfWin', N'Push', N'HalfLoss', N'Loss', N'Void')
        THROW 51020, 'Invalid Bot G2026 settlement result.', 1;
    IF @OutcomeAvailableUtc > @SettledAtUtc
        THROW 51021, 'Outcome availability cannot be later than settlement.', 1;
    IF NOT EXISTS
       (
           SELECT 1
           FROM dbo.AutomatedBotPickEvaluations
           WHERE AutomatedBotPickEvaluationId = @CandidateId
             AND BotKey = N'G2026'
       )
        THROW 51023, 'Bot G2026 candidate was not found.', 1;
    IF EXISTS
       (
           SELECT 1
           FROM dbo.AutomatedBotPickEvaluations
           WHERE AutomatedBotPickEvaluationId = @CandidateId
             AND BotKey = N'G2026'
             AND @OutcomeAvailableUtc <= PredictionTimestampUtc
       )
        THROW 51024, 'Outcome availability must occur after prediction time.', 1;
    IF @ClosingCapturedAtUtc IS NOT NULL AND EXISTS
       (
           SELECT 1 FROM dbo.AutomatedBotPickEvaluations
           WHERE AutomatedBotPickEvaluationId = @CandidateId
             AND BotKey = N'G2026'
             AND @ClosingCapturedAtUtc <= PredictionTimestampUtc
       )
        THROW 51022, 'Closing capture must occur after prediction time.', 1;

    UPDATE dbo.AutomatedBotPickEvaluations
    SET Result = @Result,
        ActualValue = @ActualValue,
        SettlementFactor = @SettlementFactor,
        ProfitLoss = @ProfitLoss,
        SettlementState = @Result,
        SettlementSource = @SettlementSource,
        SettlementSnapshotJson = @SettlementSnapshotJson,
        OutcomeAvailableUtc = @OutcomeAvailableUtc,
        SettledAtUtc = @SettledAtUtc,
        ClosingLine = @ClosingLine,
        ClosingOdds = @ClosingOdds,
        ClosingMarketNoVigProbability = @ClosingMarketNoVigProbability,
        ClosingCapturedAtUtc = @ClosingCapturedAtUtc,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE AutomatedBotPickEvaluationId = @CandidateId
      AND BotKey = N'G2026';

    IF @@ROWCOUNT = 0
        THROW 51023, 'Bot G2026 candidate was not found.', 1;

    EXEC dbo.sp_GetBotG2026CandidateDetail @CandidateId = @CandidateId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_SettleBotG2026PendingCandidates
    @OutcomeAvailableThroughUtc DATETIME2(3) = NULL,
    @MaximumCandidates INT = 5000,
    @DryRun BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @MaximumCandidates < 1 SET @MaximumCandidates = 1;
    IF @MaximumCandidates > 50000 SET @MaximumCandidates = 50000;

    DECLARE @SettledAtUtc DATETIME2(3) = SYSUTCDATETIME();
    DECLARE @OutcomeCutoffUtc DATETIME2(3) =
        COALESCE(@OutcomeAvailableThroughUtc, @SettledAtUtc);

    DECLARE @Pending TABLE
    (
        CandidateId BIGINT NOT NULL PRIMARY KEY,
        FixtureId BIGINT NOT NULL,
        PredictionTimestampUtc DATETIME2(3) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        Selection NVARCHAR(10) NOT NULL,
        LineValue DECIMAL(6,2) NOT NULL,
        SelectedOdds DECIMAL(10,4) NOT NULL,
        StakeUnits DECIMAL(9,4) NOT NULL
    );

    INSERT INTO @Pending
    (
        CandidateId, FixtureId, PredictionTimestampUtc, MarketType, Selection, LineValue,
        SelectedOdds, StakeUnits
    )
    SELECT TOP (@MaximumCandidates)
        evaluation.AutomatedBotPickEvaluationId,
        evaluation.ApiFootballFixtureId,
        evaluation.PredictionTimestampUtc,
        evaluation.MarketType,
        evaluation.SelectedSide,
        evaluation.LineValue,
        evaluation.SelectedOdds,
        COALESCE(evaluation.StakeUnits, 1.0000)
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'G2026'
      AND evaluation.Result = N'Pending'
      AND evaluation.ApiFootballFixtureId IS NOT NULL
      AND evaluation.ApiFootballFixtureId > 0
      AND evaluation.PredictionTimestampUtc IS NOT NULL
      AND evaluation.MarketType IN (N'TotalGoals', N'HomeTeamGoals', N'AwayTeamGoals')
      AND evaluation.SelectedSide IN (N'Over', N'Under')
      AND evaluation.SelectedOdds > 1
    ORDER BY evaluation.MatchDate, evaluation.AutomatedBotPickEvaluationId;

    DECLARE @Work TABLE
    (
        CandidateId BIGINT NOT NULL PRIMARY KEY,
        MatchHistoryId BIGINT NOT NULL,
        FixtureId BIGINT NOT NULL,
        ActualValue DECIMAL(10,4) NOT NULL,
        SettlementFactor DECIMAL(9,4) NOT NULL,
        Result NVARCHAR(20) NOT NULL,
        ProfitLoss DECIMAL(12,4) NOT NULL,
        OutcomeAvailableUtc DATETIME2(3) NOT NULL,
        SettlementSnapshotJson NVARCHAR(MAX) NOT NULL
    );

    ;WITH OfficialMatches AS
    (
        SELECT
            pending.*,
            MatchHistoryId = CONVERT(BIGINT, history.Id),
            history.HomeGoals,
            history.AwayGoals,
            OutcomeAvailableUtc = COALESCE(history.ApiFootballUpdatedAtUtc, @SettledAtUtc),
            OfficialMatchCount = COUNT_BIG(*) OVER (PARTITION BY pending.CandidateId)
        FROM @Pending AS pending
        INNER JOIN dbo.MatchHistory AS history
          ON history.ApiFootballFixtureId = pending.FixtureId
        WHERE history.ApiFootballFixtureId IS NOT NULL
          AND UPPER(LTRIM(RTRIM(COALESCE(history.FixtureStatus, N'')))) IN (N'FT', N'AET', N'PEN')
          AND ISNULL(history.ApiFootballGoalsAvailable, 0) = 1
          AND history.HomeGoals IS NOT NULL
          AND history.AwayGoals IS NOT NULL
          AND COALESCE(history.ApiFootballUpdatedAtUtc, @SettledAtUtc) <= @OutcomeCutoffUtc
          AND COALESCE(history.ApiFootballUpdatedAtUtc, @SettledAtUtc) > pending.PredictionTimestampUtc
    ),
    Actuals AS
    (
        SELECT
            match.CandidateId,
            match.MatchHistoryId,
            match.FixtureId,
            match.Selection,
            match.LineValue,
            match.SelectedOdds,
            match.StakeUnits,
            match.HomeGoals,
            match.AwayGoals,
            match.OutcomeAvailableUtc,
            ActualValue = CONVERT(DECIMAL(10,4), CASE match.MarketType
                WHEN N'TotalGoals' THEN match.HomeGoals + match.AwayGoals
                WHEN N'HomeTeamGoals' THEN match.HomeGoals
                WHEN N'AwayTeamGoals' THEN match.AwayGoals
            END)
        FROM OfficialMatches AS match
        WHERE match.OfficialMatchCount = 1
    ),
    Factors AS
    (
        SELECT
            actual.*,
            SettlementFactor = CONVERT(DECIMAL(9,4), AVG(CONVERT(DECIMAL(9,4),
                CASE actual.Selection
                    WHEN N'Over' THEN
                        CASE WHEN actual.ActualValue > split.SplitLine THEN 1.0
                             WHEN actual.ActualValue = split.SplitLine THEN 0.0
                             ELSE -1.0 END
                    WHEN N'Under' THEN
                        CASE WHEN actual.ActualValue < split.SplitLine THEN 1.0
                             WHEN actual.ActualValue = split.SplitLine THEN 0.0
                             ELSE -1.0 END
                END)))
        FROM Actuals AS actual
        CROSS APPLY
        (
            VALUES
                (CASE WHEN actual.LineValue - FLOOR(actual.LineValue) IN (0.25, 0.75)
                    THEN actual.LineValue - 0.25 ELSE actual.LineValue END),
                (CASE WHEN actual.LineValue - FLOOR(actual.LineValue) IN (0.25, 0.75)
                    THEN actual.LineValue + 0.25 ELSE actual.LineValue END)
        ) AS split(SplitLine)
        GROUP BY
            actual.CandidateId, actual.MatchHistoryId, actual.FixtureId,
            actual.Selection, actual.LineValue, actual.SelectedOdds,
            actual.StakeUnits, actual.HomeGoals, actual.AwayGoals,
            actual.OutcomeAvailableUtc, actual.ActualValue
    )
    INSERT INTO @Work
    (
        CandidateId, MatchHistoryId, FixtureId, ActualValue,
        SettlementFactor, Result, ProfitLoss, OutcomeAvailableUtc,
        SettlementSnapshotJson
    )
    SELECT
        factor.CandidateId,
        factor.MatchHistoryId,
        factor.FixtureId,
        factor.ActualValue,
        factor.SettlementFactor,
        CASE factor.SettlementFactor
            WHEN 1.0000 THEN N'Win'
            WHEN 0.5000 THEN N'HalfWin'
            WHEN 0.0000 THEN N'Push'
            WHEN -0.5000 THEN N'HalfLoss'
            ELSE N'Loss'
        END,
        CONVERT(DECIMAL(12,4), CASE factor.SettlementFactor
            WHEN 1.0000 THEN factor.StakeUnits * (factor.SelectedOdds - 1.0)
            WHEN 0.5000 THEN factor.StakeUnits * (factor.SelectedOdds - 1.0) / 2.0
            WHEN 0.0000 THEN 0.0
            WHEN -0.5000 THEN -factor.StakeUnits / 2.0
            ELSE -factor.StakeUnits
        END),
        factor.OutcomeAvailableUtc,
        CONCAT(
            N'{"matchHistoryId":', factor.MatchHistoryId,
            N',"apiFootballFixtureId":', factor.FixtureId,
            N',"homeGoals":', factor.HomeGoals,
            N',"awayGoals":', factor.AwayGoals,
            N',"linkMethod":"ApiFootballFixtureId"}'
        )
    FROM Factors AS factor;

    DECLARE @SettledCandidates INT = 0;
    IF @DryRun = 0
    BEGIN
        UPDATE evaluation
        SET Result = work.Result,
            ActualValue = work.ActualValue,
            SettlementFactor = work.SettlementFactor,
            ProfitLoss = work.ProfitLoss,
            SettlementState = work.Result,
            SettlementSource = N'MatchHistory:ApiFootballFixtureId',
            SettlementSnapshotJson = work.SettlementSnapshotJson,
            OutcomeAvailableUtc = work.OutcomeAvailableUtc,
            SettledAtUtc = @SettledAtUtc,
            UpdatedAtUtc = @SettledAtUtc
        FROM dbo.AutomatedBotPickEvaluations AS evaluation
        INNER JOIN @Work AS work
          ON work.CandidateId = evaluation.AutomatedBotPickEvaluationId
        WHERE evaluation.BotKey = N'G2026'
          AND evaluation.Result = N'Pending';

        SET @SettledCandidates = @@ROWCOUNT;
    END;

    SELECT
        ScannedCandidates = (SELECT COUNT(*) FROM @Pending),
        EligibleCandidates = (SELECT COUNT(*) FROM @Work),
        SettledCandidates = @SettledCandidates,
        UnmatchedOrUnavailableCandidates =
            (SELECT COUNT(*) FROM @Pending) - (SELECT COUNT(*) FROM @Work),
        RemainingPendingCandidates =
            (SELECT COUNT_BIG(*) FROM dbo.AutomatedBotPickEvaluations
             WHERE BotKey = N'G2026' AND Result = N'Pending'),
        DryRun = @DryRun,
        SettledAtUtc = @SettledAtUtc;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotG2026Scorecard
    @DateFromUtc DATETIME2(0) = NULL,
    @DateToUtc DATETIME2(0) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Read every candidate once from the narrow covering index.  Do not expand
    -- the complete set fivefold: GROUPING SETS computes the same Overall,
    -- market, side, bookmaker, and configuration rows directly over this heap.
    SELECT
        evaluation.AutomatedBotPickEvaluationId AS CandidateId,
        evaluation.FixtureIdentity AS FixtureId,
        evaluation.MatchDate AS FixtureDateUtc,
        evaluation.MarketType,
        evaluation.SelectedSide AS Selection,
        COALESCE(evaluation.Bookmaker, evaluation.Source) AS Bookmaker,
        evaluation.ConfigurationVersion,
        evaluation.Decision,
        evaluation.Published,
        evaluation.Result,
        evaluation.StakeUnits,
        evaluation.ProfitLoss,
        evaluation.SelectedOdds,
        evaluation.RawEdge,
        evaluation.ConservativeEdge,
        evaluation.RawExpectedValue,
        evaluation.ConservativeExpectedValue,
        evaluation.UncertaintyScore,
        evaluation.CalibrationReliability,
        evaluation.OutOfDistributionScore,
        evaluation.CalibratedProbability,
        evaluation.MarketNoVigProbability,
        evaluation.ClosingOdds,
        evaluation.ClosingLine,
        evaluation.LineValue AS Line,
        CASE
            WHEN evaluation.Result IN (N'Win', N'HalfWin') THEN 1.0
            WHEN evaluation.Result IN (N'Push', N'HalfLoss', N'Loss') THEN 0.0
            ELSE NULL
        END AS OutcomeScore,
        CASE
            WHEN evaluation.Result IN (N'Win', N'HalfWin', N'Push', N'HalfLoss', N'Loss')
                THEN 1 ELSE 0
        END AS IsResolved
    INTO #CandidateBase
    FROM dbo.AutomatedBotPickEvaluations AS evaluation
    WHERE evaluation.BotKey = N'G2026'
      AND (@DateFromUtc IS NULL OR evaluation.MatchDate >= @DateFromUtc)
      AND (@DateToUtc IS NULL OR evaluation.MatchDate < @DateToUtc)
      AND (@ConfigurationVersion IS NULL OR evaluation.ConfigurationVersion = @ConfigurationVersion)
    OPTION (RECOMPILE);

    ;WITH Aggregated AS
    (
        SELECT
            CASE
                WHEN GROUPING(MarketType) = 0 THEN N'MarketType'
                WHEN GROUPING(Selection) = 0 THEN N'Selection'
                WHEN GROUPING(Bookmaker) = 0 THEN N'Bookmaker'
                WHEN GROUPING(ConfigurationVersion) = 0 THEN N'ConfigurationVersion'
                ELSE N'Overall'
            END AS Dimension,
            CASE
                WHEN GROUPING(MarketType) = 0 THEN MarketType
                WHEN GROUPING(Selection) = 0 THEN Selection
                WHEN GROUPING(Bookmaker) = 0 THEN Bookmaker
                WHEN GROUPING(ConfigurationVersion) = 0 THEN ConfigurationVersion
                ELSE N'All'
            END AS Segment,
            COUNT_BIG(*) AS CandidatesEvaluated,
            COUNT_BIG(DISTINCT FixtureId) AS FixturesEvaluated,
            COUNT_BIG(DISTINCT CASE WHEN IsResolved = 1 THEN FixtureId END) AS ResolvedFixtures,
            SUM(IIF(Decision = N'Approved', 1, 0)) AS CandidatesApproved,
            SUM(IIF(Decision = N'Rejected', 1, 0)) AS CandidatesRejected,
            SUM(IIF(Decision = N'Abstain', 1, 0)) AS CandidatesAbstained,
            SUM(IIF(Published = 1, 1, 0)) AS CandidatesPublished,
            SUM(IIF(Decision = N'Approved' AND IsResolved = 1, 1, 0)) AS Resolved,
            SUM(IIF(IsResolved = 1, 1, 0)) AS PredictiveResolved,
            SUM(IIF(Decision = N'Approved' AND Result = N'Win', 1, 0)) AS Won,
            SUM(IIF(Decision = N'Approved' AND Result = N'HalfWin', 1, 0)) AS HalfWon,
            SUM(IIF(Decision = N'Approved' AND Result = N'Push', 1, 0)) AS Pushes,
            SUM(IIF(Decision = N'Approved' AND Result = N'HalfLoss', 1, 0)) AS HalfLost,
            SUM(IIF(Decision = N'Approved' AND Result = N'Loss', 1, 0)) AS Lost,
            SUM(IIF(Decision = N'Approved' AND Result = N'Void', 1, 0)) AS Voids,
            SUM
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1
                    THEN COALESCE(StakeUnits, 1.0) ELSE 0 END
            ) AS Stake,
            SUM
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1
                    THEN COALESCE(ProfitLoss, 0.0) ELSE 0 END
            ) AS ProfitLoss,
            AVG(CASE WHEN Decision = N'Approved' AND IsResolved = 1 THEN SelectedOdds END) AS AverageOdds,
            AVG(RawEdge) AS AverageRawEdge,
            AVG(ConservativeEdge) AS AverageConservativeEdge,
            AVG(RawExpectedValue) AS AverageRawExpectedValue,
            AVG(ConservativeExpectedValue) AS AverageConservativeExpectedValue,
            AVG(UncertaintyScore) AS AverageUncertainty,
            AVG(CalibrationReliability) AS AverageCalibrationReliability,
            AVG(OutOfDistributionScore) AS AverageOutOfDistributionScore,
            AVG
            (
                CASE WHEN OutcomeScore IS NOT NULL AND CalibratedProbability IS NOT NULL
                    THEN POWER(CONVERT(FLOAT, CalibratedProbability) - OutcomeScore, 2) END
            ) AS Brier,
            AVG
            (
                CASE WHEN OutcomeScore IS NOT NULL AND MarketNoVigProbability IS NOT NULL
                    THEN POWER(CONVERT(FLOAT, MarketNoVigProbability) - OutcomeScore, 2) END
            ) AS MarketBrier,
            AVG
            (
                CASE WHEN OutcomeScore IS NOT NULL AND CalibratedProbability IS NOT NULL THEN
                    -OutcomeScore * LOG
                    (
                        CASE
                            WHEN CalibratedProbability < 0.000001 THEN 0.000001
                            WHEN CalibratedProbability > 0.999999 THEN 0.999999
                            ELSE CONVERT(FLOAT, CalibratedProbability)
                        END
                    )
                    -(1.0 - OutcomeScore) * LOG
                    (
                        CASE
                            WHEN CalibratedProbability < 0.000001 THEN 0.999999
                            WHEN CalibratedProbability > 0.999999 THEN 0.000001
                            ELSE 1.0 - CONVERT(FLOAT, CalibratedProbability)
                        END
                    )
                END
            ) AS LogLoss,
            AVG
            (
                CASE WHEN OutcomeScore IS NOT NULL AND MarketNoVigProbability IS NOT NULL THEN
                    -OutcomeScore * LOG
                    (
                        CASE
                            WHEN MarketNoVigProbability < 0.000001 THEN 0.000001
                            WHEN MarketNoVigProbability > 0.999999 THEN 0.999999
                            ELSE CONVERT(FLOAT, MarketNoVigProbability)
                        END
                    )
                    -(1.0 - OutcomeScore) * LOG
                    (
                        CASE
                            WHEN MarketNoVigProbability < 0.000001 THEN 0.999999
                            WHEN MarketNoVigProbability > 0.999999 THEN 0.000001
                            ELSE 1.0 - CONVERT(FLOAT, MarketNoVigProbability)
                        END
                    )
                END
            ) AS MarketLogLoss,
            SUM
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1 AND ProfitLoss > 0
                    THEN ProfitLoss ELSE 0 END
            ) AS GrossProfit,
            ABS(SUM
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1 AND ProfitLoss < 0
                    THEN ProfitLoss ELSE 0 END
            )) AS GrossLoss,
            AVG
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1
                           AND ClosingOdds IS NOT NULL AND ClosingOdds > 1
                    THEN CONVERT(FLOAT, SelectedOdds) / CONVERT(FLOAT, ClosingOdds) - 1.0 END
            ) AS AverageOddsClv,
            AVG
            (
                CASE WHEN Decision = N'Approved' AND IsResolved = 1 AND ClosingLine IS NOT NULL
                    THEN CASE WHEN Selection = N'Over'
                        THEN CONVERT(FLOAT, ClosingLine - Line)
                        ELSE CONVERT(FLOAT, Line - ClosingLine) END END
            ) AS AverageLineClv
        FROM #CandidateBase
        GROUP BY GROUPING SETS
        (
            (),
            (MarketType),
            (Selection),
            (Bookmaker),
            (ConfigurationVersion)
        )
    ),
    CalibrationBins AS
    (
        SELECT
            CASE
                WHEN GROUPING(MarketType) = 0 THEN N'MarketType'
                WHEN GROUPING(Selection) = 0 THEN N'Selection'
                WHEN GROUPING(Bookmaker) = 0 THEN N'Bookmaker'
                WHEN GROUPING(ConfigurationVersion) = 0 THEN N'ConfigurationVersion'
                ELSE N'Overall'
            END AS Dimension,
            CASE
                WHEN GROUPING(MarketType) = 0 THEN MarketType
                WHEN GROUPING(Selection) = 0 THEN Selection
                WHEN GROUPING(Bookmaker) = 0 THEN Bookmaker
                WHEN GROUPING(ConfigurationVersion) = 0 THEN ConfigurationVersion
                ELSE N'All'
            END AS Segment,
            CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            )) AS BinNumber,
            COUNT_BIG(*) AS BinCount,
            AVG(CONVERT(FLOAT, CalibratedProbability)) AS MeanProbability,
            AVG(OutcomeScore) AS MeanOutcome
        FROM #CandidateBase
        WHERE OutcomeScore IS NOT NULL AND CalibratedProbability IS NOT NULL
        GROUP BY GROUPING SETS
        (
            (CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            ))),
            (MarketType, CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            ))),
            (Selection, CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            ))),
            (Bookmaker, CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            ))),
            (ConfigurationVersion, CONVERT(INT, FLOOR
            (
                CASE WHEN CalibratedProbability >= 1 THEN 9
                    ELSE CONVERT(FLOAT, CalibratedProbability) * 10 END
            )))
        )
    ),
    Calibration AS
    (
        SELECT
            Dimension,
            Segment,
            SUM(CONVERT(FLOAT, BinCount) * ABS(MeanProbability - MeanOutcome)) /
                NULLIF(SUM(CONVERT(FLOAT, BinCount)), 0) AS Ece
        FROM CalibrationBins
        GROUP BY Dimension, Segment
    ),
    EconomicExpanded AS
    (
        SELECT
            segment.Dimension,
            segment.Segment,
            candidate.CandidateId,
            candidate.FixtureDateUtc,
            candidate.ProfitLoss
        FROM #CandidateBase AS candidate
        CROSS APPLY
        (
            VALUES
                (N'Overall', N'All'),
                (N'MarketType', candidate.MarketType),
                (N'Selection', candidate.Selection),
                (N'Bookmaker', candidate.Bookmaker),
                (N'ConfigurationVersion', candidate.ConfigurationVersion)
        ) AS segment(Dimension, Segment)
        WHERE candidate.Decision = N'Approved' AND candidate.IsResolved = 1
    ),
    EconomicOrdered AS
    (
        SELECT
            Dimension,
            Segment,
            CandidateId,
            FixtureDateUtc,
            SUM(COALESCE(ProfitLoss, 0.0)) OVER
            (
                PARTITION BY Dimension, Segment
                ORDER BY FixtureDateUtc, CandidateId
                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
            ) AS RunningProfit
        FROM EconomicExpanded
    ),
    Drawdowns AS
    (
        SELECT
            Dimension,
            Segment,
            RunningProfit,
            MAX(RunningProfit) OVER
            (
                PARTITION BY Dimension, Segment
                ORDER BY FixtureDateUtc, CandidateId
                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
            ) AS RunningPeak
        FROM EconomicOrdered
    ),
    Risk AS
    (
        SELECT Dimension, Segment, MAX(RunningPeak - RunningProfit) AS MaximumDrawdown
        FROM Drawdowns
        GROUP BY Dimension, Segment
    )
    SELECT
        aggregate.Dimension,
        aggregate.Segment,
        aggregate.CandidatesEvaluated,
        aggregate.FixturesEvaluated,
        aggregate.ResolvedFixtures,
        aggregate.CandidatesApproved,
        aggregate.CandidatesRejected,
        aggregate.CandidatesAbstained,
        aggregate.CandidatesPublished,
        aggregate.Resolved,
        aggregate.PredictiveResolved,
        aggregate.Won,
        aggregate.HalfWon,
        aggregate.Pushes,
        aggregate.HalfLost,
        aggregate.Lost,
        aggregate.Voids,
        aggregate.Stake,
        aggregate.ProfitLoss,
        aggregate.ProfitLoss / NULLIF(CONVERT(FLOAT, aggregate.Stake), 0) AS Yield,
        CONVERT(FLOAT, aggregate.Won + aggregate.HalfWon) /
            NULLIF(CONVERT(FLOAT, aggregate.Resolved), 0) AS HitRate,
        aggregate.AverageOdds,
        aggregate.AverageRawEdge,
        aggregate.AverageConservativeEdge,
        aggregate.AverageRawExpectedValue,
        aggregate.AverageConservativeExpectedValue,
        aggregate.ProfitLoss / NULLIF(CONVERT(FLOAT, aggregate.Stake), 0) AS ActualYield,
        aggregate.AverageConservativeExpectedValue -
            aggregate.ProfitLoss / NULLIF(CONVERT(FLOAT, aggregate.Stake), 0) AS ExpectedValueYieldGap,
        aggregate.Brier,
        aggregate.MarketBrier,
        aggregate.Brier - aggregate.MarketBrier AS DeltaBrier,
        aggregate.LogLoss,
        aggregate.MarketLogLoss,
        aggregate.LogLoss - aggregate.MarketLogLoss AS DeltaLogLoss,
        calibration.Ece,
        CAST(NULL AS FLOAT) AS CalibrationSlope,
        CAST(NULL AS FLOAT) AS CalibrationIntercept,
        COALESCE(risk.MaximumDrawdown, 0.0) AS MaximumDrawdown,
        aggregate.GrossProfit / NULLIF(CONVERT(FLOAT, aggregate.GrossLoss), 0) AS ProfitFactor,
        CONVERT(FLOAT, aggregate.CandidatesApproved) /
            NULLIF(CONVERT(FLOAT, aggregate.CandidatesEvaluated), 0) AS CoverageRate,
        CONVERT(FLOAT, aggregate.CandidatesPublished) /
            NULLIF(CONVERT(FLOAT, aggregate.CandidatesApproved), 0) AS PublicationRate,
        aggregate.AverageOddsClv,
        aggregate.AverageLineClv,
        aggregate.AverageUncertainty,
        aggregate.AverageCalibrationReliability,
        aggregate.AverageOutOfDistributionScore,
        CASE
            WHEN aggregate.ResolvedFixtures < 30 THEN N'SHADOW'
            WHEN aggregate.ResolvedFixtures < 100 THEN N'EXPERIMENTAL'
            ELSE N'MONITORING'
        END AS SuggestedPromotionStage
    FROM Aggregated AS aggregate
    LEFT JOIN Calibration AS calibration
      ON calibration.Dimension = aggregate.Dimension
     AND calibration.Segment = aggregate.Segment
    LEFT JOIN Risk AS risk
      ON risk.Dimension = aggregate.Dimension
     AND risk.Segment = aggregate.Segment
    ORDER BY
        CASE aggregate.Dimension
            WHEN N'Overall' THEN 0
            WHEN N'MarketType' THEN 1
            WHEN N'Selection' THEN 2
            WHEN N'Bookmaker' THEN 3
            ELSE 4
        END,
        aggregate.Segment;
END;

GO

-- Calibration slope/intercept require an iterative logistic fit and are kept
-- NULL in the online SQL scorecard rather than reporting a misleading OLS
-- approximation. The reproducible offline evaluator supplies those metrics.
-- Likewise, CANDIDATE/PRODUCTION require an independently verified second OOS
-- walk-forward window. SQL has no trustworthy experiment-window lineage, so the
-- online scorecard deliberately caps its suggestion at MONITORING.
