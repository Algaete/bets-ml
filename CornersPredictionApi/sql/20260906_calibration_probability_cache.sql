SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Derived cache only. Historical rows are filled on demand after official
-- outcomes have been resolved; no full-ledger JSON backfill is needed.
IF OBJECT_ID(N'dbo.AutomatedBotCalibrationProbabilityCache',N'U') IS NULL
    CREATE TABLE dbo.AutomatedBotCalibrationProbabilityCache
    (
        EvaluationId BIGINT NOT NULL CONSTRAINT PK_AutomatedBotCalibrationProbabilityCache PRIMARY KEY,
        SourceHash BINARY(32) NOT NULL,
        SourceProbability FLOAT NULL,
        CachedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_BotCalibrationCache_CachedAt DEFAULT SYSUTCDATETIME()
    );
GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedBotPickEvaluations_InvalidateCalibrationCache
ON dbo.AutomatedBotPickEvaluations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    DELETE cached
    FROM dbo.AutomatedBotCalibrationProbabilityCache AS cached
    INNER JOIN deleted AS previous ON previous.AutomatedBotPickEvaluationId = cached.EvaluationId;
END;
GO
