CREATE OR ALTER PROCEDURE dbo.sp_VoidAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Reason NVARCHAR(500) = N'Pick anulado manualmente.';

    UPDATE dbo.AutomatedCornerBetSelections
    SET Status = N'Void',
        ActualHomeCorners = NULL,
        ActualAwayCorners = NULL,
        ActualTotalCorners = NULL,
        SettlementActualValue = NULL,
        SettlementFactor = CONVERT(DECIMAL(6,3), 0),
        SettlementReason = @Reason,
        SettlementSource = N'Manual',
        SettlementSnapshotJson = N'{"source":"Manual","action":"Void"}',
        LastSettlementCheckReason = @Reason,
        LastSettlementCheckAtUtc = @NowUtc,
        ProfitLoss = 0,
        YieldPct = 0,
        SettledAtUtc = @NowUtc,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateAutomatedCornerBetSelectionStatus
    @AutomatedCornerBetSelectionId BIGINT,
    @Status NVARCHAR(20),
    @ActualHomeCorners INT = NULL,
    @ActualAwayCorners INT = NULL,
    @ActualTotalCorners INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Status NOT IN (N'Pending', N'Won', N'Lost', N'Push', N'Void')
        THROW 50000, 'Invalid status. Allowed: Pending, Won, Lost, Push, Void.', 1;

    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();
    DECLARE @Reason NVARCHAR(500) = CASE
        WHEN @Status = N'Pending' THEN N'Pick reabierto manualmente.'
        ELSE CONCAT(N'Estado establecido manualmente: ', @Status, N'.')
    END;

    UPDATE dbo.AutomatedCornerBetSelections
    SET Status = @Status,
        ActualHomeCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualHomeCorners, ActualHomeCorners) END,
        ActualAwayCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualAwayCorners, ActualAwayCorners) END,
        ActualTotalCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualTotalCorners, ActualTotalCorners) END,
        SettlementActualValue = CASE WHEN @Status = N'Pending' THEN NULL ELSE SettlementActualValue END,
        SettlementFactor = CASE
            WHEN @Status = N'Pending' THEN NULL
            WHEN @Status = N'Won' THEN CONVERT(DECIMAL(6,3), 1)
            WHEN @Status = N'Lost' THEN CONVERT(DECIMAL(6,3), -1)
            ELSE CONVERT(DECIMAL(6,3), 0)
        END,
        SettlementReason = CASE WHEN @Status = N'Pending' THEN NULL ELSE @Reason END,
        SettlementSource = CASE WHEN @Status = N'Pending' THEN NULL ELSE N'Manual' END,
        SettlementSnapshotJson = CASE
            WHEN @Status = N'Pending' THEN NULL
            ELSE CONCAT(N'{"source":"Manual","action":"Status","status":"', @Status, N'"}')
        END,
        LastSettlementCheckReason = @Reason,
        LastSettlementCheckAtUtc = @NowUtc,
        ProfitLoss = CASE
            WHEN @Status = N'Won' THEN ROUND(Stake * (Odds - 1), 2)
            WHEN @Status = N'Lost' THEN ROUND(-Stake, 2)
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        YieldPct = CASE
            WHEN @Status = N'Won' THEN ROUND(Odds - 1, 4)
            WHEN @Status = N'Lost' THEN -1
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        SettledAtUtc = CASE WHEN @Status = N'Pending' THEN NULL ELSE @NowUtc END,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_ResolveAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @ActualValue INT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ActualValue < 0
        THROW 50000, 'Actual result must be zero or greater.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.AutomatedCornerBetSelections
        WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId
    )
    BEGIN
        SET @RowsAffected = 0;
        RETURN;
    END;

    DECLARE @SettlementFactor DECIMAL(6,3);
    DECLARE @SelectedSide NVARCHAR(10);
    DECLARE @LineValue DECIMAL(6,2);
    DECLARE @NowUtc DATETIME2(0) = SYSUTCDATETIME();

    SELECT @SelectedSide = SelectedSide,
           @LineValue = LineValue,
           @SettlementFactor = CAST(
               CASE
                   WHEN LineValue - FLOOR(LineValue) = 0.25
                    AND @ActualValue = FLOOR(LineValue)
                    AND SelectedSide = N'Over' THEN -0.5
                   WHEN LineValue - FLOOR(LineValue) = 0.25
                    AND @ActualValue = FLOOR(LineValue)
                    AND SelectedSide = N'Under' THEN 0.5
                   WHEN LineValue - FLOOR(LineValue) = 0.75
                    AND @ActualValue = CEILING(LineValue)
                    AND SelectedSide = N'Over' THEN 0.5
                   WHEN LineValue - FLOOR(LineValue) = 0.75
                    AND @ActualValue = CEILING(LineValue)
                    AND SelectedSide = N'Under' THEN -0.5
                   WHEN @ActualValue = LineValue THEN 0
                   WHEN SelectedSide = N'Over' AND @ActualValue > LineValue THEN 1
                   WHEN SelectedSide = N'Under' AND @ActualValue < LineValue THEN 1
                   ELSE -1
               END AS DECIMAL(6,3))
    FROM dbo.AutomatedCornerBetSelections
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    DECLARE @SettlementReason NVARCHAR(500) = CONCAT(
        N'Liquidación manual: ', @SelectedSide, N' ', CONVERT(NVARCHAR(30), @LineValue),
        N', resultado ', CONVERT(NVARCHAR(20), @ActualValue),
        N', factor ', CONVERT(NVARCHAR(20), @SettlementFactor), N'.');

    UPDATE dbo.AutomatedCornerBetSelections
    SET ActualHomeCorners = CASE
            WHEN MarketType IN (N'HomeTeamCorners', N'HomeTeamGoals', N'HomeTeamShots', N'HomeTeamShotsOnGoal')
                THEN @ActualValue
            ELSE ActualHomeCorners
        END,
        ActualAwayCorners = CASE
            WHEN MarketType IN (N'AwayTeamCorners', N'AwayTeamGoals', N'AwayTeamShots', N'AwayTeamShotsOnGoal')
                THEN @ActualValue
            ELSE ActualAwayCorners
        END,
        ActualTotalCorners = CASE
            WHEN MarketType IN (N'TotalCorners', N'TotalGoals', N'TotalShots', N'TotalShotsOnGoal')
                THEN @ActualValue
            ELSE ActualTotalCorners
        END,
        SettlementActualValue = @ActualValue,
        SettlementFactor = @SettlementFactor,
        SettlementReason = @SettlementReason,
        SettlementSource = N'Manual',
        SettlementSnapshotJson = CONCAT(
            N'{"source":"Manual","actualValue":', CONVERT(NVARCHAR(20), @ActualValue),
            N',"factor":', CONVERT(NVARCHAR(20), @SettlementFactor), N'}'),
        LastSettlementCheckReason = @SettlementReason,
        LastSettlementCheckAtUtc = @NowUtc,
        Status = CASE
            WHEN @SettlementFactor > 0 THEN N'Won'
            WHEN @SettlementFactor < 0 THEN N'Lost'
            ELSE N'Push'
        END,
        ProfitLoss = CASE
            WHEN @SettlementFactor > 0 THEN ROUND(Stake * (Odds - 1) * @SettlementFactor, 2)
            WHEN @SettlementFactor < 0 THEN ROUND(Stake * @SettlementFactor, 2)
            ELSE 0
        END,
        YieldPct = CASE
            WHEN Stake = 0 THEN NULL
            WHEN @SettlementFactor > 0 THEN ROUND((Odds - 1) * @SettlementFactor, 4)
            ELSE @SettlementFactor
        END,
        SettledAtUtc = @NowUtc,
        UpdatedAtUtc = @NowUtc
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO
