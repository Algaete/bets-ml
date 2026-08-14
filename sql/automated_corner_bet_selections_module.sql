IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'ApiFootballFixtureId') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD ApiFootballFixtureId BIGINT NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'MatchHistoryId') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD MatchHistoryId BIGINT NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementActualValue') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementActualValue INT NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementFactor') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementFactor DECIMAL(6,3) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementReason') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementReason NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementSource') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementSource NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementMatchStatus') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementMatchStatus NVARCHAR(20) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'SettlementSnapshotJson') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD SettlementSnapshotJson NVARCHAR(MAX) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'LastSettlementCheckReason') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD LastSettlementCheckReason NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'LastSettlementCheckAtUtc') IS NULL
        ALTER TABLE dbo.AutomatedCornerBetSelections ADD LastSettlementCheckAtUtc DATETIME2(0) NULL;

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
          AND name = N'CK_AutomatedCornerBetSelections_Status'
    )
        ALTER TABLE dbo.AutomatedCornerBetSelections DROP CONSTRAINT CK_AutomatedCornerBetSelections_Status;

    ALTER TABLE dbo.AutomatedCornerBetSelections WITH CHECK
        ADD CONSTRAINT CK_AutomatedCornerBetSelections_Status
        CHECK (Status IN (N'Pending', N'Won', N'Lost', N'Push', N'Void'));
END;

GO

IF OBJECT_ID(N'dbo.CornerOddsSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CornerOddsSnapshots
    (
        CornerOddsSnapshotId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_CornerOddsSnapshots PRIMARY KEY,
        CapturedAtUtc DATETIME2(3) NOT NULL,
        Source NVARCHAR(50) NOT NULL,
        SourceMatchId NVARCHAR(100) NULL,
        SourceUrl NVARCHAR(1000) NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(300) NOT NULL,
        StandardizedLeague NVARCHAR(300) NULL,
        HomeTeam NVARCHAR(300) NOT NULL,
        AwayTeam NVARCHAR(300) NOT NULL,
        StandardizedHomeTeam NVARCHAR(300) NULL,
        StandardizedAwayTeam NVARCHAR(300) NULL,
        HomeTeamGender NVARCHAR(1) NOT NULL,
        AwayTeamGender NVARCHAR(1) NOT NULL,
        MarketType NVARCHAR(50) NOT NULL,
        LineValue DECIMAL(10,2) NOT NULL,
        OverOdds DECIMAL(18,6) NULL,
        UnderOdds DECIMAL(18,6) NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_CornerOddsSnapshots_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_CornerOddsSnapshots_MatchMarketCapture'
      AND object_id = OBJECT_ID(N'dbo.CornerOddsSnapshots')
)
BEGIN
    CREATE INDEX IX_CornerOddsSnapshots_MatchMarketCapture
        ON dbo.CornerOddsSnapshots(Source, MatchDate, MarketType, LineValue, CapturedAtUtc)
        INCLUDE
        (
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            OverOdds,
            UnderOdds
        );
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedCornerBetSelections
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL,
    @Status NVARCHAR(20) = NULL,
    @League NVARCHAR(200) = NULL,
    @Source NVARCHAR(50) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @OnlyPending BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.AutomatedCornerBetSelectionId,
        s.RunId,
        s.AutomationVersion,
        s.Source,
        s.SourceMatchId,
        s.ApiFootballFixtureId,
        s.MatchHistoryId,
        s.SourceUrl,
        s.MatchDate,
        MatchDay = CAST(s.MatchDate AS DATE),
        s.League,
        s.StandardizedLeague,
        s.HomeTeam,
        s.AwayTeam,
        s.StandardizedHomeTeam,
        s.StandardizedAwayTeam,
        s.SourceMarketType,
        s.MarketType,
        Recommendation = CONCAT(s.SelectedSide, ' ', CONVERT(VARCHAR(20), s.LineValue)),
        s.SelectedSide,
        s.LineValue,
        s.Odds,
        s.Stake,
        s.FlatStake,
        s.KellyFraction,
        s.ImpliedProbability,
        s.ModelProbability,
        s.ProbabilityEdge,
        s.ExpectedValue,
        s.SelectionScore,
        s.PredictedTotalCorners,
        s.PredTotalDirect,
        s.PredHomeCorners,
        s.PredAwayCorners,
        s.PredTotalCombined,
        s.DistanceToLine,
        s.ConfidenceLevel,
        s.OverUnderConfidenceLevel,
        s.ModelConsensus,
        s.ContextTotalCorners,
        s.ContextDifference,
        s.RecommendedSide,
        s.Status,
        s.ActualHomeCorners,
        s.ActualAwayCorners,
        s.ActualTotalCorners,
        s.SettlementActualValue,
        s.SettlementFactor,
        s.SettlementReason,
        s.SettlementSource,
        s.SettlementMatchStatus,
        s.LastSettlementCheckReason,
        s.LastSettlementCheckAtUtc,
        s.ProfitLoss,
        s.YieldPct,
        s.DecisionReason,
        s.CreatedAtUtc,
        s.UpdatedAtUtc,
        s.SettledAtUtc
    FROM dbo.AutomatedCornerBetSelections s
    WHERE (@DateFrom IS NULL OR CAST(s.MatchDate AS DATE) >= @DateFrom)
      AND (@DateTo IS NULL OR CAST(s.MatchDate AS DATE) <= @DateTo)
      AND (@Status IS NULL OR s.Status = @Status)
      AND (@League IS NULL OR COALESCE(s.StandardizedLeague, s.League) = @League)
      AND (@Source IS NULL OR s.Source = @Source)
      AND (@MarketType IS NULL OR s.MarketType = @MarketType)
      AND (@OnlyPending = 0 OR s.Status = N'Pending')
    ORDER BY
        CASE WHEN s.Status = N'Pending' THEN 0 ELSE 1 END,
        s.MatchDate ASC,
        s.SelectionScore DESC,
        s.AutomatedCornerBetSelectionId DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_VoidAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        Status = N'Void',
        ActualHomeCorners = NULL,
        ActualAwayCorners = NULL,
        ActualTotalCorners = NULL,
        ProfitLoss = 0,
        YieldPct = 0,
        SettledAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteAutomatedCornerBetSelection
    @AutomatedCornerBetSelectionId BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.AutomatedCornerBetSelections
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
    BEGIN
        THROW 50000, 'Invalid status. Allowed: Pending, Won, Lost, Push, Void.', 1;
    END;

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        Status = @Status,
        ActualHomeCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualHomeCorners, ActualHomeCorners) END,
        ActualAwayCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualAwayCorners, ActualAwayCorners) END,
        ActualTotalCorners = CASE WHEN @Status = N'Pending' THEN NULL ELSE COALESCE(@ActualTotalCorners, ActualTotalCorners) END,
        ProfitLoss = CASE
            WHEN @Status = N'Won' THEN ROUND(Stake * (Odds - 1), 2)
            WHEN @Status = N'Lost' THEN ROUND(-Stake, 2)
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        YieldPct = CASE
            WHEN @Status = N'Won' THEN ROUND(((Stake * (Odds - 1)) / NULLIF(Stake, 0)) * 100, 4)
            WHEN @Status = N'Lost' THEN -100
            WHEN @Status IN (N'Push', N'Void') THEN 0
            ELSE NULL
        END,
        SettledAtUtc = CASE
            WHEN @Status = N'Pending' THEN NULL
            ELSE SYSUTCDATETIME()
        END,
        UpdatedAtUtc = SYSUTCDATETIME()
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

    IF @ActualValue < 0
    BEGIN
        THROW 50000, 'Actual result must be zero or greater.', 1;
    END;

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

    DECLARE @SettlementFactor DECIMAL(4,2);

    SELECT
        @SettlementFactor =
            CAST(
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
                END
                AS DECIMAL(4,2))
    FROM dbo.AutomatedCornerBetSelections
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    UPDATE dbo.AutomatedCornerBetSelections
    SET
        ActualHomeCorners = CASE
            WHEN MarketType IN (N'HomeTeamCorners', N'HomeTeamGoals', N'HomeTeamShots', N'HomeTeamShotsOnGoal')
                THEN @ActualValue
            ELSE NULL
        END,
        ActualAwayCorners = CASE
            WHEN MarketType IN (N'AwayTeamCorners', N'AwayTeamGoals', N'AwayTeamShots', N'AwayTeamShotsOnGoal')
                THEN @ActualValue
            ELSE NULL
        END,
        ActualTotalCorners = CASE
            WHEN MarketType IN (N'TotalCorners', N'TotalGoals', N'TotalShots', N'TotalShotsOnGoal')
                THEN @ActualValue
            ELSE NULL
        END,
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
        SettledAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE AutomatedCornerBetSelectionId = @AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;
