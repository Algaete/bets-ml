SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.AutomatedRecommendationJobs', N'CurrentStage') IS NULL
    ALTER TABLE dbo.AutomatedRecommendationJobs ADD CurrentStage NVARCHAR(250) NULL;
IF COL_LENGTH(N'dbo.AutomatedRecommendationJobs', N'CurrentBatchCompletedMatches') IS NULL
    ALTER TABLE dbo.AutomatedRecommendationJobs ADD CurrentBatchCompletedMatches INT NOT NULL
        CONSTRAINT DF_RecommendationJobs_CurrentCompleted DEFAULT 0;
IF COL_LENGTH(N'dbo.AutomatedRecommendationJobs', N'CurrentBatchTotalMatches') IS NULL
    ALTER TABLE dbo.AutomatedRecommendationJobs ADD CurrentBatchTotalMatches INT NOT NULL
        CONSTRAINT DF_RecommendationJobs_CurrentTotal DEFAULT 0;
IF COL_LENGTH(N'dbo.AutomatedRecommendationJobs', N'LastProgressAtUtc') IS NULL
    ALTER TABLE dbo.AutomatedRecommendationJobs ADD LastProgressAtUtc DATETIME2(0) NULL;
GO

CREATE OR ALTER VIEW dbo.vw_AutomatedRecommendationJobs
AS
    SELECT RecommendationJobId, Name, Status, Mode, DateFrom, DateTo, BotKeys,
        MarketFamilies, BatchSize, NextBatchNumber, TotalBatches, ProcessedBatches,
        SelectedMatches, InsertedRows, UpdatedRows, SkippedMatches, ErrorMatches,
        AttemptCount, MaxAttempts, LastRunId, LastError, CreatedAtUtc, StartedAtUtc,
        UpdatedAtUtc, CompletedAtUtc, CurrentStage, CurrentBatchCompletedMatches,
        CurrentBatchTotalMatches, LastProgressAtUtc
    FROM dbo.AutomatedRecommendationJobs;
GO
