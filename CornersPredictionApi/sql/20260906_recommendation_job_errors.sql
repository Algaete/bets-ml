-- Preserve diagnostic errors when a job finishes or advances to another batch.
CREATE OR ALTER PROCEDURE dbo.sp_CompleteAutomatedRecommendationJobBatch
    @RecommendationJobId UNIQUEIDENTIFIER,
    @WorkerId NVARCHAR(150),
    @CompletedBatchNumber INT,
    @TotalBatches INT,
    @RunId UNIQUEIDENTIFIER,
    @SelectedMatches INT,
    @InsertedRows INT,
    @UpdatedRows INT,
    @SkippedMatches INT,
    @ErrorMatches INT,
    @ErrorSummary NVARCHAR(2000) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsComplete BIT = CASE WHEN @TotalBatches <= @CompletedBatchNumber THEN 1 ELSE 0 END;

    UPDATE dbo.AutomatedRecommendationJobs
    SET
        Status = CASE WHEN @IsComplete = 1 THEN N'Completed' ELSE N'Queued' END,
        NextBatchNumber = @CompletedBatchNumber + 1,
        TotalBatches = @TotalBatches,
        ProcessedBatches = ProcessedBatches + 1,
        SelectedMatches = SelectedMatches + @SelectedMatches,
        InsertedRows = InsertedRows + @InsertedRows,
        UpdatedRows = UpdatedRows + @UpdatedRows,
        SkippedMatches = SkippedMatches + @SkippedMatches,
        ErrorMatches = ErrorMatches + @ErrorMatches,
        AttemptCount = 0,
        LastRunId = @RunId,
        LastError = CASE
            WHEN @ErrorMatches > 0 THEN LEFT(CONCAT(
                CASE WHEN ErrorMatches > 0 AND LastError IS NOT NULL THEN CONCAT(LastError, CHAR(10)) ELSE N'' END,
                COALESCE(NULLIF(@ErrorSummary,N''), CONCAT(N'El lote ',@CompletedBatchNumber,N' terminó con ',@ErrorMatches,N' errores.'))), 2000)
            WHEN ErrorMatches > 0 THEN LastError ELSE NULL END,
        LeaseOwner = NULL,
        LeaseExpiresAtUtc = NULL,
        NextAttemptAtUtc = NULL,
        UpdatedAtUtc = SYSUTCDATETIME(),
        CompletedAtUtc = CASE WHEN @IsComplete = 1 THEN SYSUTCDATETIME() ELSE NULL END
    WHERE RecommendationJobId = @RecommendationJobId
      AND Status = N'Running'
      AND LeaseOwner = @WorkerId;

    SELECT *
    FROM dbo.vw_AutomatedRecommendationJobs
    WHERE RecommendationJobId = @RecommendationJobId;
END;
