IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AutomatedCornerBetSelections
    (
        AutomatedCornerBetSelectionId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AutomatedCornerBetSelections PRIMARY KEY,
        RunId UNIQUEIDENTIFIER NOT NULL,
        AutomationVersion NVARCHAR(50) NOT NULL,
        Source NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_Source DEFAULT N'Betano',
        SourceMatchId NVARCHAR(100) NULL,
        SourceUrl NVARCHAR(500) NULL,
        MatchDate DATETIME2(0) NOT NULL,
        League NVARCHAR(200) NOT NULL,
        StandardizedLeague NVARCHAR(200) NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        StandardizedHomeTeam NVARCHAR(150) NULL,
        StandardizedAwayTeam NVARCHAR(150) NULL,
        HomeTeamGender CHAR(1) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_HomeTeamGender DEFAULT 'M',
        AwayTeamGender CHAR(1) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_AwayTeamGender DEFAULT 'M',
        SourceMarketType NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_SourceMarketType DEFAULT N'CornersTotal',
        MarketType NVARCHAR(50) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_MarketType DEFAULT N'TotalCorners',
        LineValue DECIMAL(6,2) NOT NULL,
        SelectedSide NVARCHAR(10) NOT NULL,
        Odds DECIMAL(10,2) NOT NULL,
        Stake DECIMAL(10,2) NOT NULL,
        FlatStake DECIMAL(10,2) NULL,
        ImpliedProbability DECIMAL(9,6) NULL,
        ModelProbability DECIMAL(9,6) NULL,
        ProbabilityEdge DECIMAL(9,6) NULL,
        ExpectedValue DECIMAL(9,6) NULL,
        KellyFraction DECIMAL(9,6) NULL,
        SelectionScore DECIMAL(9,6) NULL,
        PredictedTotalCorners DECIMAL(9,4) NULL,
        PredTotalDirect DECIMAL(9,4) NULL,
        PredHomeCorners DECIMAL(9,4) NULL,
        PredAwayCorners DECIMAL(9,4) NULL,
        PredTotalCombined DECIMAL(9,4) NULL,
        DistanceToLine DECIMAL(9,4) NULL,
        ConfidenceLevel NVARCHAR(20) NULL,
        OverUnderConfidenceLevel NVARCHAR(20) NULL,
        ModelConsensus NVARCHAR(20) NULL,
        ContextTotalCorners DECIMAL(9,4) NULL,
        ContextDifference DECIMAL(9,4) NULL,
        RecommendedSide NVARCHAR(10) NULL,
        Status NVARCHAR(20) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_Status DEFAULT N'Pending',
        ActualHomeCorners INT NULL,
        ActualAwayCorners INT NULL,
        ActualTotalCorners INT NULL,
        ProfitLoss DECIMAL(10,2) NULL,
        YieldPct DECIMAL(9,4) NULL,
        DecisionReason NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_AutomatedCornerBetSelections_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        SettledAtUtc DATETIME2(0) NULL,
        CONSTRAINT CK_AutomatedCornerBetSelections_Side CHECK (SelectedSide IN (N'Over', N'Under')),
        CONSTRAINT CK_AutomatedCornerBetSelections_Status CHECK (Status IN (N'Pending', N'Won', N'Lost', N'Void'))
    );
END;

GO

IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'FlatStake') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedCornerBetSelections
        ADD FlatStake DECIMAL(10,2) NULL;
END;

IF COL_LENGTH(N'dbo.AutomatedCornerBetSelections', N'KellyFraction') IS NULL
BEGIN
    ALTER TABLE dbo.AutomatedCornerBetSelections
        ADD KellyFraction DECIMAL(9,6) NULL;
END;

GO

UPDATE dbo.AutomatedCornerBetSelections
SET FlatStake = Stake
WHERE FlatStake IS NULL;

GO

;WITH RankedDuplicateSelections AS
(
    SELECT
        AutomatedCornerBetSelectionId,
        rn = ROW_NUMBER() OVER
        (
            PARTITION BY
                AutomationVersion,
                Source,
                MarketType,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(SourceMatchId)), N'') IS NOT NULL
                        THEN CONCAT(N'ID|', Source, N'|', LTRIM(RTRIM(SourceMatchId)))
                    WHEN NULLIF(LTRIM(RTRIM(SourceUrl)), N'') IS NOT NULL
                        THEN CONCAT(N'URL|', Source, N'|', LTRIM(RTRIM(SourceUrl)))
                    ELSE CONCAT(
                        N'FALLBACK|',
                        Source,
                        N'|',
                        CONVERT(NVARCHAR(19), MatchDate, 126),
                        N'|',
                        COALESCE(StandardizedLeague, League),
                        N'|',
                        COALESCE(StandardizedHomeTeam, HomeTeam),
                        N'|',
                        COALESCE(StandardizedAwayTeam, AwayTeam))
                END
            ORDER BY
                CASE WHEN Status IN (N'Won', N'Lost', N'Void') THEN 0 ELSE 1 END,
                UpdatedAtUtc DESC,
                AutomatedCornerBetSelectionId DESC
        )
    FROM dbo.AutomatedCornerBetSelections
)
DELETE s
FROM dbo.AutomatedCornerBetSelections s
INNER JOIN RankedDuplicateSelections d
    ON d.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId
WHERE d.rn > 1;

GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_AutomatedCornerBetSelections_Match'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE UNIQUE INDEX UX_AutomatedCornerBetSelections_Match
        ON dbo.AutomatedCornerBetSelections
        (
            AutomationVersion,
            Source,
            MatchDate,
            StandardizedLeague,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            MarketType
        );
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_AutomatedCornerBetSelections_Match'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
      AND NOT EXISTS
      (
          SELECT 1
          FROM sys.index_columns ic
          INNER JOIN sys.columns c
              ON c.object_id = ic.object_id
             AND c.column_id = ic.column_id
          WHERE ic.object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
            AND ic.index_id =
            (
                SELECT index_id
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
                  AND name = N'UX_AutomatedCornerBetSelections_Match'
            )
            AND c.name = N'AutomationVersion'
            AND ic.key_ordinal = 1
      )
)
BEGIN
    DROP INDEX UX_AutomatedCornerBetSelections_Match
        ON dbo.AutomatedCornerBetSelections;

    CREATE UNIQUE INDEX UX_AutomatedCornerBetSelections_Match
        ON dbo.AutomatedCornerBetSelections
        (
            AutomationVersion,
            Source,
            MatchDate,
            StandardizedLeague,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            MarketType
        );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AutomatedCornerBetSelections_StatusDate'
      AND object_id = OBJECT_ID(N'dbo.AutomatedCornerBetSelections')
)
BEGIN
    CREATE INDEX IX_AutomatedCornerBetSelections_StatusDate
        ON dbo.AutomatedCornerBetSelections(Status, MatchDate)
        INCLUDE (League, StandardizedLeague, HomeTeam, AwayTeam, LineValue, Odds, Stake, ProfitLoss);
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertAutomatedCornerBetSelection
    @RunId UNIQUEIDENTIFIER,
    @AutomationVersion NVARCHAR(50),
    @Source NVARCHAR(50),
    @SourceMatchId NVARCHAR(100) = NULL,
    @SourceUrl NVARCHAR(500) = NULL,
    @MatchDate DATETIME2(0),
    @League NVARCHAR(200),
    @StandardizedLeague NVARCHAR(200) = NULL,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @StandardizedHomeTeam NVARCHAR(150) = NULL,
    @StandardizedAwayTeam NVARCHAR(150) = NULL,
    @HomeTeamGender CHAR(1) = 'M',
    @AwayTeamGender CHAR(1) = 'M',
    @SourceMarketType NVARCHAR(50) = N'CornersTotal',
    @MarketType NVARCHAR(50) = N'TotalCorners',
    @LineValue DECIMAL(6,2),
    @SelectedSide NVARCHAR(10),
    @Odds DECIMAL(10,2),
    @Stake DECIMAL(10,2),
    @FlatStake DECIMAL(10,2) = NULL,
    @ImpliedProbability DECIMAL(9,6) = NULL,
    @ModelProbability DECIMAL(9,6) = NULL,
    @ProbabilityEdge DECIMAL(9,6) = NULL,
    @ExpectedValue DECIMAL(9,6) = NULL,
    @KellyFraction DECIMAL(9,6) = NULL,
    @SelectionScore DECIMAL(9,6) = NULL,
    @PredictedTotalCorners DECIMAL(9,4) = NULL,
    @PredTotalDirect DECIMAL(9,4) = NULL,
    @PredHomeCorners DECIMAL(9,4) = NULL,
    @PredAwayCorners DECIMAL(9,4) = NULL,
    @PredTotalCombined DECIMAL(9,4) = NULL,
    @DistanceToLine DECIMAL(9,4) = NULL,
    @ConfidenceLevel NVARCHAR(20) = NULL,
    @OverUnderConfidenceLevel NVARCHAR(20) = NULL,
    @ModelConsensus NVARCHAR(20) = NULL,
    @ContextTotalCorners DECIMAL(9,4) = NULL,
    @ContextDifference DECIMAL(9,4) = NULL,
    @RecommendedSide NVARCHAR(10) = NULL,
    @DecisionReason NVARCHAR(MAX) = NULL,
    @AutomatedCornerBetSelectionId BIGINT OUTPUT,
    @MergeAction NVARCHAR(10) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Results TABLE
    (
        AutomatedCornerBetSelectionId BIGINT NOT NULL,
        MergeAction NVARCHAR(10) NOT NULL
    );

    MERGE dbo.AutomatedCornerBetSelections AS Target
    USING
    (
        SELECT
            RunId = @RunId,
            AutomationVersion = @AutomationVersion,
            Source = @Source,
            SourceMatchId = @SourceMatchId,
            SourceUrl = @SourceUrl,
            MatchDate = @MatchDate,
            League = @League,
            StandardizedLeague = @StandardizedLeague,
            HomeTeam = @HomeTeam,
            AwayTeam = @AwayTeam,
            StandardizedHomeTeam = @StandardizedHomeTeam,
            StandardizedAwayTeam = @StandardizedAwayTeam,
            HomeTeamGender = @HomeTeamGender,
            AwayTeamGender = @AwayTeamGender,
            SourceMarketType = @SourceMarketType,
            MarketType = @MarketType,
            LineValue = @LineValue,
            SelectedSide = @SelectedSide,
            Odds = @Odds,
            Stake = @Stake,
            FlatStake = COALESCE(@FlatStake, @Stake),
            ImpliedProbability = @ImpliedProbability,
            ModelProbability = @ModelProbability,
            ProbabilityEdge = @ProbabilityEdge,
            ExpectedValue = @ExpectedValue,
            KellyFraction = @KellyFraction,
            SelectionScore = @SelectionScore,
            PredictedTotalCorners = @PredictedTotalCorners,
            PredTotalDirect = @PredTotalDirect,
            PredHomeCorners = @PredHomeCorners,
            PredAwayCorners = @PredAwayCorners,
            PredTotalCombined = @PredTotalCombined,
            DistanceToLine = @DistanceToLine,
            ConfidenceLevel = @ConfidenceLevel,
            OverUnderConfidenceLevel = @OverUnderConfidenceLevel,
            ModelConsensus = @ModelConsensus,
            ContextTotalCorners = @ContextTotalCorners,
            ContextDifference = @ContextDifference,
            RecommendedSide = @RecommendedSide,
            DecisionReason = @DecisionReason
    ) AS Source
        ON Target.AutomationVersion = Source.AutomationVersion
       AND Target.Source = Source.Source
       AND Target.MarketType = Source.MarketType
       AND
       (
            (
                NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NOT NULL
                AND Target.SourceMatchId = Source.SourceMatchId
            )
            OR
            (
                NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NULL
                AND NULLIF(LTRIM(RTRIM(Source.SourceUrl)), N'') IS NOT NULL
                AND Target.SourceUrl = Source.SourceUrl
            )
            OR
            (
                NULLIF(LTRIM(RTRIM(Source.SourceMatchId)), N'') IS NULL
                AND NULLIF(LTRIM(RTRIM(Source.SourceUrl)), N'') IS NULL
                AND Target.MatchDate = Source.MatchDate
                AND COALESCE(Target.StandardizedLeague, Target.League) = COALESCE(Source.StandardizedLeague, Source.League)
                AND COALESCE(Target.StandardizedHomeTeam, Target.HomeTeam) = COALESCE(Source.StandardizedHomeTeam, Source.HomeTeam)
                AND COALESCE(Target.StandardizedAwayTeam, Target.AwayTeam) = COALESCE(Source.StandardizedAwayTeam, Source.AwayTeam)
            )
       )
    WHEN MATCHED THEN
        UPDATE SET
            Target.RunId = Source.RunId,
            Target.AutomationVersion = Source.AutomationVersion,
            Target.SourceMatchId = Source.SourceMatchId,
            Target.SourceUrl = Source.SourceUrl,
            Target.League = Source.League,
            Target.StandardizedLeague = Source.StandardizedLeague,
            Target.HomeTeam = Source.HomeTeam,
            Target.AwayTeam = Source.AwayTeam,
            Target.StandardizedHomeTeam = Source.StandardizedHomeTeam,
            Target.StandardizedAwayTeam = Source.StandardizedAwayTeam,
            Target.HomeTeamGender = Source.HomeTeamGender,
            Target.AwayTeamGender = Source.AwayTeamGender,
            Target.SourceMarketType = Source.SourceMarketType,
            Target.LineValue = Source.LineValue,
            Target.SelectedSide = Source.SelectedSide,
            Target.Odds = Source.Odds,
            Target.Stake = Source.Stake,
            Target.FlatStake = Source.FlatStake,
            Target.ImpliedProbability = Source.ImpliedProbability,
            Target.ModelProbability = Source.ModelProbability,
            Target.ProbabilityEdge = Source.ProbabilityEdge,
            Target.ExpectedValue = Source.ExpectedValue,
            Target.KellyFraction = Source.KellyFraction,
            Target.SelectionScore = Source.SelectionScore,
            Target.PredictedTotalCorners = Source.PredictedTotalCorners,
            Target.PredTotalDirect = Source.PredTotalDirect,
            Target.PredHomeCorners = Source.PredHomeCorners,
            Target.PredAwayCorners = Source.PredAwayCorners,
            Target.PredTotalCombined = Source.PredTotalCombined,
            Target.DistanceToLine = Source.DistanceToLine,
            Target.ConfidenceLevel = Source.ConfidenceLevel,
            Target.OverUnderConfidenceLevel = Source.OverUnderConfidenceLevel,
            Target.ModelConsensus = Source.ModelConsensus,
            Target.ContextTotalCorners = Source.ContextTotalCorners,
            Target.ContextDifference = Source.ContextDifference,
            Target.RecommendedSide = Source.RecommendedSide,
            Target.DecisionReason = Source.DecisionReason,
            Target.Status = CASE WHEN Target.Status IN (N'Won', N'Lost', N'Void') THEN Target.Status ELSE N'Pending' END,
            Target.UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            RunId,
            AutomationVersion,
            Source,
            SourceMatchId,
            SourceUrl,
            MatchDate,
            League,
            StandardizedLeague,
            HomeTeam,
            AwayTeam,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            HomeTeamGender,
            AwayTeamGender,
            SourceMarketType,
            MarketType,
            LineValue,
            SelectedSide,
            Odds,
            Stake,
            FlatStake,
            ImpliedProbability,
            ModelProbability,
            ProbabilityEdge,
            ExpectedValue,
            KellyFraction,
            SelectionScore,
            PredictedTotalCorners,
            PredTotalDirect,
            PredHomeCorners,
            PredAwayCorners,
            PredTotalCombined,
            DistanceToLine,
            ConfidenceLevel,
            OverUnderConfidenceLevel,
            ModelConsensus,
            ContextTotalCorners,
            ContextDifference,
            RecommendedSide,
            DecisionReason,
            Status,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            Source.RunId,
            Source.AutomationVersion,
            Source.Source,
            Source.SourceMatchId,
            Source.SourceUrl,
            Source.MatchDate,
            Source.League,
            Source.StandardizedLeague,
            Source.HomeTeam,
            Source.AwayTeam,
            Source.StandardizedHomeTeam,
            Source.StandardizedAwayTeam,
            Source.HomeTeamGender,
            Source.AwayTeamGender,
            Source.SourceMarketType,
            Source.MarketType,
            Source.LineValue,
            Source.SelectedSide,
            Source.Odds,
            Source.Stake,
            Source.FlatStake,
            Source.ImpliedProbability,
            Source.ModelProbability,
            Source.ProbabilityEdge,
            Source.ExpectedValue,
            Source.KellyFraction,
            Source.SelectionScore,
            Source.PredictedTotalCorners,
            Source.PredTotalDirect,
            Source.PredHomeCorners,
            Source.PredAwayCorners,
            Source.PredTotalCombined,
            Source.DistanceToLine,
            Source.ConfidenceLevel,
            Source.OverUnderConfidenceLevel,
            Source.ModelConsensus,
            Source.ContextTotalCorners,
            Source.ContextDifference,
            Source.RecommendedSide,
            Source.DecisionReason,
            N'Pending',
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        )
    OUTPUT inserted.AutomatedCornerBetSelectionId, $action
        INTO @Results (AutomatedCornerBetSelectionId, MergeAction);

    SELECT TOP (1)
        @AutomatedCornerBetSelectionId = AutomatedCornerBetSelectionId,
        @MergeAction = MergeAction
    FROM @Results;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAutomatedCornerBetSelections
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL,
    @Status NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        AutomatedCornerBetSelectionId,
        RunId,
        AutomationVersion,
        Source,
        SourceMatchId,
        SourceUrl,
        MatchDate,
        League,
        StandardizedLeague,
        HomeTeam,
        AwayTeam,
        StandardizedHomeTeam,
        StandardizedAwayTeam,
        HomeTeamGender,
        AwayTeamGender,
        SourceMarketType,
        MarketType,
        LineValue,
        SelectedSide,
        Odds,
        Stake,
        FlatStake,
        ImpliedProbability,
        ModelProbability,
        ProbabilityEdge,
        ExpectedValue,
        KellyFraction,
        SelectionScore,
        PredictedTotalCorners,
        PredTotalDirect,
        PredHomeCorners,
        PredAwayCorners,
        PredTotalCombined,
        DistanceToLine,
        ConfidenceLevel,
        OverUnderConfidenceLevel,
        ModelConsensus,
        ContextTotalCorners,
        ContextDifference,
        RecommendedSide,
        Status,
        ActualHomeCorners,
        ActualAwayCorners,
        ActualTotalCorners,
        ProfitLoss,
        YieldPct,
        DecisionReason,
        CreatedAtUtc,
        UpdatedAtUtc,
        SettledAtUtc
    FROM dbo.AutomatedCornerBetSelections
    WHERE (@DateFrom IS NULL OR CAST(MatchDate AS DATE) >= @DateFrom)
      AND (@DateTo IS NULL OR CAST(MatchDate AS DATE) <= @DateTo)
      AND (@Status IS NULL OR Status = @Status)
    ORDER BY MatchDate DESC, UpdatedAtUtc DESC, AutomatedCornerBetSelectionId DESC;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_SettleAutomatedCornerBetSelections
    @MatchDateTo DATE = NULL,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH MatchesToSettle AS
    (
        SELECT
            s.AutomatedCornerBetSelectionId,
            mh.HomeCorners,
            mh.AwayCorners,
            ActualTotalCorners = mh.HomeCorners + mh.AwayCorners,
            ActualSelectedCorners =
                CASE s.MarketType
                    WHEN N'HomeTeamCorners' THEN mh.HomeCorners
                    WHEN N'AwayTeamCorners' THEN mh.AwayCorners
                    ELSE mh.HomeCorners + mh.AwayCorners
                END
        FROM dbo.AutomatedCornerBetSelections s
        INNER JOIN dbo.MatchHistory mh
            ON CAST(s.MatchDate AS DATE) = mh.MatchDate
           AND COALESCE(s.StandardizedLeague, s.League) = COALESCE(mh.StandardizedLeague, mh.League)
           AND COALESCE(s.StandardizedHomeTeam, s.HomeTeam) = COALESCE(mh.StandardizedHomeTeam, mh.HomeTeam)
           AND COALESCE(s.StandardizedAwayTeam, s.AwayTeam) = COALESCE(mh.StandardizedAwayTeam, mh.AwayTeam)
        WHERE s.Status = N'Pending'
          AND (@MatchDateTo IS NULL OR CAST(s.MatchDate AS DATE) <= @MatchDateTo)
    )
    UPDATE s
    SET
        ActualHomeCorners = m.HomeCorners,
        ActualAwayCorners = m.AwayCorners,
        ActualTotalCorners = m.ActualTotalCorners,
        Status =
            CASE
                WHEN s.SelectedSide = N'Over' AND m.ActualSelectedCorners > s.LineValue THEN N'Won'
                WHEN s.SelectedSide = N'Over' AND m.ActualSelectedCorners < s.LineValue THEN N'Lost'
                WHEN s.SelectedSide = N'Under' AND m.ActualSelectedCorners < s.LineValue THEN N'Won'
                WHEN s.SelectedSide = N'Under' AND m.ActualSelectedCorners > s.LineValue THEN N'Lost'
                ELSE N'Void'
            END,
        ProfitLoss =
            CASE
                WHEN s.SelectedSide = N'Over' AND m.ActualSelectedCorners > s.LineValue THEN ROUND(s.Stake * (s.Odds - 1), 2)
                WHEN s.SelectedSide = N'Under' AND m.ActualSelectedCorners < s.LineValue THEN ROUND(s.Stake * (s.Odds - 1), 2)
                WHEN m.ActualSelectedCorners = s.LineValue THEN 0
                ELSE ROUND(-1 * s.Stake, 2)
            END,
        YieldPct =
            CASE
                WHEN s.Stake = 0 THEN NULL
                WHEN s.SelectedSide = N'Over' AND m.ActualSelectedCorners > s.LineValue THEN ROUND((s.Stake * (s.Odds - 1)) / s.Stake, 4)
                WHEN s.SelectedSide = N'Under' AND m.ActualSelectedCorners < s.LineValue THEN ROUND((s.Stake * (s.Odds - 1)) / s.Stake, 4)
                WHEN m.ActualSelectedCorners = s.LineValue THEN 0
                ELSE -1
            END,
        SettledAtUtc = SYSUTCDATETIME(),
        UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.AutomatedCornerBetSelections s
    INNER JOIN MatchesToSettle m
        ON m.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId;

    SET @RowsAffected = @@ROWCOUNT;
END;

GO
