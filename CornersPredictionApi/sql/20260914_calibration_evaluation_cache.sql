SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Compact, rebuildable projection of source evaluations. Official outcomes
-- and fixture links are deliberately resolved live, so corrections are visible.
-- No full audit/JSON scan is performed by this migration.
IF OBJECT_ID(N'dbo.AutomatedBotCalibrationEvaluationCache', N'U') IS NULL
BEGIN
    SELECT TOP (0)
        AutomatedBotPickEvaluationId = ISNULL(CONVERT(BIGINT, e.AutomatedBotPickEvaluationId), 0),
        e.BotKey, e.Decision, e.ApiFootballFixtureId, e.PublishedSelectionId,
        e.MatchDate, e.HomeTeam, e.AwayTeam, e.MarketType, e.SelectedSide,
        e.LineValue, e.SelectedOdds, e.BaseCalibratedProbability,
        e.MarketNoVigProbability, e.DataQualityScore,
        e.BaseModelTrainedThroughUtc, e.BaseModelVersion
    INTO dbo.AutomatedBotCalibrationEvaluationCache
    FROM dbo.AutomatedBotPickEvaluations AS e;
END;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedBotCalibrationEvaluationCache') AND type = 'PK')
BEGIN
    ALTER TABLE dbo.AutomatedBotCalibrationEvaluationCache
        ALTER COLUMN AutomatedBotPickEvaluationId BIGINT NOT NULL;
    EXEC(N'ALTER TABLE dbo.AutomatedBotCalibrationEvaluationCache
        ADD CONSTRAINT PK_AutomatedBotCalibrationEvaluationCache
        PRIMARY KEY CLUSTERED (AutomatedBotPickEvaluationId);');
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotCalibrationEvaluationCache')
      AND name = N'IX_CalibrationEvaluationCache_Source')
BEGIN
    CREATE INDEX IX_CalibrationEvaluationCache_Source
        ON dbo.AutomatedBotCalibrationEvaluationCache(BotKey, Decision, MatchDate)
        INCLUDE (ApiFootballFixtureId, PublishedSelectionId, HomeTeam, AwayTeam, MarketType,
            SelectedSide, LineValue, SelectedOdds, BaseCalibratedProbability, MarketNoVigProbability,
            DataQualityScore, BaseModelTrainedThroughUtc, BaseModelVersion)
        WITH (DATA_COMPRESSION = PAGE);
END;
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.AutomatedBotCalibrationEvaluationCache')
      AND i.name=N'IX_CalibrationEvaluationCache_Source' AND c.name=N'BaseModelVersion'
)
    CREATE INDEX IX_CalibrationEvaluationCache_Source
        ON dbo.AutomatedBotCalibrationEvaluationCache(BotKey, Decision, MatchDate)
        INCLUDE (ApiFootballFixtureId, PublishedSelectionId, HomeTeam, AwayTeam, MarketType,
            SelectedSide, LineValue, SelectedOdds, BaseCalibratedProbability, MarketNoVigProbability,
            DataQualityScore, BaseModelTrainedThroughUtc, BaseModelVersion)
        WITH (DROP_EXISTING = ON, DATA_COMPRESSION = PAGE);
COMMIT TRANSACTION;
GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedBotPickEvaluations_InvalidateCalibrationEvaluationCache
ON dbo.AutomatedBotPickEvaluations
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DELETE cached
    FROM dbo.AutomatedBotCalibrationEvaluationCache AS cached
    INNER JOIN deleted AS previous
      ON previous.AutomatedBotPickEvaluationId = cached.AutomatedBotPickEvaluationId;

    -- Prepare new selector evidence while its row is already in memory. Later
    -- settlement only has to join official outcomes, not reread the audit blob.
    INSERT dbo.AutomatedBotCalibrationEvaluationCache
        (AutomatedBotPickEvaluationId, BotKey, Decision, ApiFootballFixtureId, PublishedSelectionId,
         MatchDate, HomeTeam, AwayTeam, MarketType, SelectedSide, LineValue, SelectedOdds,
         BaseCalibratedProbability, MarketNoVigProbability, DataQualityScore,
         BaseModelTrainedThroughUtc, BaseModelVersion)
    SELECT i.AutomatedBotPickEvaluationId, i.BotKey, i.Decision, i.ApiFootballFixtureId,
        i.PublishedSelectionId, i.MatchDate, i.HomeTeam, i.AwayTeam, i.MarketType, i.SelectedSide,
        i.LineValue, i.SelectedOdds, i.BaseCalibratedProbability, i.MarketNoVigProbability,
        i.DataQualityScore, i.BaseModelTrainedThroughUtc, i.BaseModelVersion
    FROM inserted AS i
    WHERE i.BotKey IN (N'C2026', N'D2026', N'E2026', N'F2026')
      AND i.Decision IN (N'Approved', N'Rejected')
      AND i.SelectedSide IN (N'Over', N'Under') AND i.SelectedOdds > 1;
END;
GO
