/*
    I2026 market-movement shadow collector.

    This migration is additive and idempotent. I2026 reads immutable
    CornerOddsSnapshots and appends decision-time audits. It is intentionally not
    an AutomatedBotDefinition and can never insert a productive pick. Outcomes
    are projected at query time only from later, official MatchHistory evidence.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

GO

IF OBJECT_ID(N'dbo.CornerOddsSnapshots', N'U') IS NULL
    THROW 52300, 'I2026 requires CornerOddsSnapshots.', 1;
IF OBJECT_ID(N'dbo.MatchHistory', N'U') IS NULL
    THROW 52301, 'I2026 requires MatchHistory.', 1;
IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NULL
    THROW 52302, 'I2026 publication guard requires AutomatedCornerBetSelections.', 1;
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballFixtureId') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'FixtureStatus') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballUpdatedAtUtc') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballGoalsAvailable') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballCornersAvailable') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'HomeGoals') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'AwayGoals') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'HomeCorners') IS NULL
   OR COL_LENGTH(N'dbo.MatchHistory', N'AwayCorners') IS NULL
    THROW 52303, 'I2026 requires official goal/corner MatchHistory lineage.', 1;

GO

IF OBJECT_ID(N'dbo.BotI2026ShadowEvaluations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BotI2026ShadowEvaluations
    (
        ShadowEvaluationId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BotI2026ShadowEvaluations PRIMARY KEY,
        IdempotencyKey CHAR(64) NOT NULL,
        BotKey NVARCHAR(50) NOT NULL,
        ConfigurationVersion NVARCHAR(80) NOT NULL,
        FeatureSchemaVersion NVARCHAR(80) NOT NULL,
        FixtureIdentity BIGINT NOT NULL,
        ApiFootballFixtureId BIGINT NULL,
        FixtureDateUtc DATETIME2(3) NOT NULL,
        PredictionTimestampUtc DATETIME2(3) NOT NULL,
        League NVARCHAR(300) NOT NULL,
        HomeTeam NVARCHAR(300) NOT NULL,
        AwayTeam NVARCHAR(300) NOT NULL,
        Source NVARCHAR(50) NOT NULL,
        SourceMatchId NVARCHAR(100) NULL,
        MarketType NVARCHAR(50) NOT NULL,
        Selection NVARCHAR(10) NOT NULL,
        Decision NVARCHAR(20) NOT NULL,
        SignalScore DECIMAL(12,8) NOT NULL,
        SelectedOdds DECIMAL(18,6) NOT NULL,
        OpeningSnapshotId BIGINT NOT NULL,
        CurrentSnapshotId BIGINT NOT NULL,
        PeerSnapshotId BIGINT NULL,
        OpeningCapturedAtUtc DATETIME2(3) NOT NULL,
        CurrentCapturedAtUtc DATETIME2(3) NOT NULL,
        PeerCapturedAtUtc DATETIME2(3) NULL,
        OpeningLine DECIMAL(10,2) NOT NULL,
        CurrentLine DECIMAL(10,2) NOT NULL,
        PeerLine DECIMAL(10,2) NULL,
        OpeningOverNoVigProbability DECIMAL(12,8) NOT NULL,
        CurrentOverNoVigProbability DECIMAL(12,8) NOT NULL,
        PeerOverNoVigProbability DECIMAL(12,8) NULL,
        SelectedProbabilityMovement DECIMAL(12,8) NOT NULL,
        SelectedLineMovement DECIMAL(10,2) NOT NULL,
        MovementVelocityPerHour DECIMAL(12,8) NOT NULL,
        ObservationHours DECIMAL(12,4) NOT NULL,
        OddsAgeMinutes DECIMAL(12,4) NOT NULL,
        SnapshotCount INT NOT NULL,
        PeerSource NVARCHAR(50) NULL,
        PinnacleOverNoVigProbability DECIMAL(12,8) NULL,
        BetanoOverNoVigProbability DECIMAL(12,8) NULL,
        CrossBookProbabilityDispersion DECIMAL(12,8) NULL,
        CrossBookLineDispersion DECIMAL(10,2) NULL,
        ReasonCodesJson NVARCHAR(MAX) NOT NULL,
        RiskFlagsJson NVARCHAR(MAX) NOT NULL,
        Explanation NVARCHAR(1000) NOT NULL,
        FeatureSnapshotJson NVARCHAR(MAX) NOT NULL,
        FeatureSnapshotHash BINARY(32) NOT NULL,
        ShadowOnly BIT NOT NULL CONSTRAINT DF_BotI2026ShadowEvaluations_Shadow DEFAULT (1),
        PublicationBlocked BIT NOT NULL CONSTRAINT DF_BotI2026ShadowEvaluations_Blocked DEFAULT (1),
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_BotI2026ShadowEvaluations_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BotI2026ShadowEvaluations_Idempotency UNIQUE (IdempotencyKey),
        CONSTRAINT FK_BotI2026ShadowEvaluations_OpeningSnapshot FOREIGN KEY (OpeningSnapshotId)
            REFERENCES dbo.CornerOddsSnapshots(CornerOddsSnapshotId),
        CONSTRAINT FK_BotI2026ShadowEvaluations_CurrentSnapshot FOREIGN KEY (CurrentSnapshotId)
            REFERENCES dbo.CornerOddsSnapshots(CornerOddsSnapshotId),
        CONSTRAINT FK_BotI2026ShadowEvaluations_PeerSnapshot FOREIGN KEY (PeerSnapshotId)
            REFERENCES dbo.CornerOddsSnapshots(CornerOddsSnapshotId),
        CONSTRAINT CK_BotI2026ShadowEvaluations_BotKey CHECK (BotKey = N'I2026'),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Shadow CHECK (ShadowOnly = 1 AND PublicationBlocked = 1),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Market CHECK (MarketType IN (N'TotalGoals', N'TotalCorners')),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Selection CHECK (Selection IN (N'Over', N'Under')),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Decision CHECK (Decision IN (N'Approved', N'Rejected', N'Abstain')),
        CONSTRAINT CK_BotI2026ShadowEvaluations_HalfLines CHECK
            (OpeningLine >= 0 AND CurrentLine >= 0
             AND OpeningLine * 2 = FLOOR(OpeningLine * 2)
             AND CurrentLine * 2 = FLOOR(CurrentLine * 2)
             AND OpeningLine <> FLOOR(OpeningLine)
             AND CurrentLine <> FLOOR(CurrentLine)),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Probabilities CHECK
            (OpeningOverNoVigProbability BETWEEN 0 AND 1
             AND CurrentOverNoVigProbability BETWEEN 0 AND 1
             AND (PeerOverNoVigProbability IS NULL OR PeerOverNoVigProbability BETWEEN 0 AND 1)),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Temporal CHECK
            (OpeningCapturedAtUtc <= CurrentCapturedAtUtc
             AND CurrentCapturedAtUtc <= PredictionTimestampUtc
             AND PredictionTimestampUtc < FixtureDateUtc
             AND (PeerCapturedAtUtc IS NULL OR PeerCapturedAtUtc <= PredictionTimestampUtc)),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Json CHECK
            (ISJSON(ReasonCodesJson) = 1 AND ISJSON(RiskFlagsJson) = 1 AND ISJSON(FeatureSnapshotJson) = 1),
        CONSTRAINT CK_BotI2026ShadowEvaluations_Count CHECK (SnapshotCount >= 1),
        CONSTRAINT CK_BotI2026ShadowEvaluations_SelectedOdds CHECK (SelectedOdds > 1)
    );
END;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotI2026ShadowEvaluations')
      AND name = N'IX_BotI2026ShadowEvaluations_Page'
)
BEGIN
    CREATE INDEX IX_BotI2026ShadowEvaluations_Page
        ON dbo.BotI2026ShadowEvaluations(PredictionTimestampUtc DESC, ShadowEvaluationId DESC)
        INCLUDE
        (
            ConfigurationVersion, FixtureIdentity, ApiFootballFixtureId,
            MarketType, Selection, Decision, Source, SignalScore,
            CurrentSnapshotId, CurrentLine, SelectedOdds
        );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotI2026ShadowEvaluations')
      AND name = N'IX_BotI2026ShadowEvaluations_Scorecard'
)
BEGIN
    CREATE INDEX IX_BotI2026ShadowEvaluations_Scorecard
        ON dbo.BotI2026ShadowEvaluations(ConfigurationVersion, PredictionTimestampUtc DESC)
        INCLUDE
        (
            FixtureIdentity, ApiFootballFixtureId, MarketType, Selection,
            Decision, Source, SignalScore, SelectedOdds, CurrentLine,
            SelectedProbabilityMovement, SelectedLineMovement, OddsAgeMinutes,
            ObservationHours, PeerSnapshotId
        );
END;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.BotI2026ShadowEvaluations')
      AND name = N'UX_BotI2026ShadowEvaluations_CurrentSnapshot'
)
BEGIN
    CREATE UNIQUE INDEX UX_BotI2026ShadowEvaluations_CurrentSnapshot
        ON dbo.BotI2026ShadowEvaluations(ConfigurationVersion, CurrentSnapshotId)
        INCLUDE (Decision, FeatureSnapshotHash);
END;

GO

-- The generic odds index starts with Source, while I2026 deliberately scans both
-- books by a bounded fixture-date window.  This access path avoids a full odds
-- history scan every 15 minutes.
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.CornerOddsSnapshots')
      AND name = N'IX_CornerOddsSnapshots_BotIWindow'
)
BEGIN
    CREATE INDEX IX_CornerOddsSnapshots_BotIWindow
        ON dbo.CornerOddsSnapshots(MatchDate, MarketType, CapturedAtUtc)
        INCLUDE
        (
            Source, SourceMatchId, League, StandardizedLeague,
            HomeTeam, StandardizedHomeTeam, AwayTeam, StandardizedAwayTeam,
            HomeTeamGender, AwayTeamGender, LineValue, OverOdds, UnderOdds
        );
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_AppendBotI2026ShadowEvaluation
    @IdempotencyKey CHAR(64),
    @ConfigurationVersion NVARCHAR(80),
    @FeatureSchemaVersion NVARCHAR(80),
    @FixtureIdentity BIGINT,
    @ApiFootballFixtureId BIGINT = NULL,
    @FixtureDateUtc DATETIME2(3),
    @PredictionTimestampUtc DATETIME2(3),
    @League NVARCHAR(300),
    @HomeTeam NVARCHAR(300),
    @AwayTeam NVARCHAR(300),
    @Source NVARCHAR(50),
    @SourceMatchId NVARCHAR(100) = NULL,
    @MarketType NVARCHAR(50),
    @Selection NVARCHAR(10),
    @Decision NVARCHAR(20),
    @SignalScore DECIMAL(12,8),
    @SelectedOdds DECIMAL(18,6),
    @OpeningSnapshotId BIGINT,
    @CurrentSnapshotId BIGINT,
    @PeerSnapshotId BIGINT = NULL,
    @OpeningCapturedAtUtc DATETIME2(3),
    @CurrentCapturedAtUtc DATETIME2(3),
    @PeerCapturedAtUtc DATETIME2(3) = NULL,
    @OpeningLine DECIMAL(10,2),
    @CurrentLine DECIMAL(10,2),
    @PeerLine DECIMAL(10,2) = NULL,
    @OpeningOverNoVigProbability DECIMAL(12,8),
    @CurrentOverNoVigProbability DECIMAL(12,8),
    @PeerOverNoVigProbability DECIMAL(12,8) = NULL,
    @SelectedProbabilityMovement DECIMAL(12,8),
    @SelectedLineMovement DECIMAL(10,2),
    @MovementVelocityPerHour DECIMAL(12,8),
    @ObservationHours DECIMAL(12,4),
    @OddsAgeMinutes DECIMAL(12,4),
    @SnapshotCount INT,
    @PeerSource NVARCHAR(50) = NULL,
    @PinnacleOverNoVigProbability DECIMAL(12,8) = NULL,
    @BetanoOverNoVigProbability DECIMAL(12,8) = NULL,
    @CrossBookProbabilityDispersion DECIMAL(12,8) = NULL,
    @CrossBookLineDispersion DECIMAL(10,2) = NULL,
    @ReasonCodesJson NVARCHAR(MAX),
    @RiskFlagsJson NVARCHAR(MAX),
    @Explanation NVARCHAR(1000),
    @FeatureSnapshotJson NVARCHAR(MAX),
    @FeatureSnapshotHash BINARY(32)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @ConfigurationVersion <> N'bot-i-market-movement-shadow-1.0.0'
       OR @FeatureSchemaVersion <> N'bot-i-market-movement-features-1.0.0'
        THROW 52310, 'I2026 version mismatch.', 1;
    IF @PredictionTimestampUtc >= @FixtureDateUtc
       OR @OpeningCapturedAtUtc > @CurrentCapturedAtUtc
       OR @CurrentCapturedAtUtc > @PredictionTimestampUtc
       OR (@PeerCapturedAtUtc IS NOT NULL AND @PeerCapturedAtUtc > @PredictionTimestampUtc)
        THROW 52311, 'I2026 temporal lineage is invalid.', 1;
    IF ISJSON(@ReasonCodesJson) <> 1 OR ISJSON(@RiskFlagsJson) <> 1 OR ISJSON(@FeatureSnapshotJson) <> 1
        THROW 52312, 'I2026 audit JSON is invalid.', 1;

    DECLARE @ComputedIdempotencyKey CHAR(64) = LOWER(CONVERT(VARCHAR(64), HASHBYTES(
        N'SHA2_256', CONCAT(
            N'I2026|', @ConfigurationVersion, N'|', @FixtureIdentity, N'|',
            UPPER(LTRIM(RTRIM(@Source))), N'|', @MarketType, N'|', @CurrentSnapshotId)), 2));
    IF @ComputedIdempotencyKey <> @IdempotencyKey
        THROW 52317, 'I2026 idempotency key does not match its immutable identity.', 1;
    IF HASHBYTES(N'SHA2_256', CONVERT(VARBINARY(MAX), @FeatureSnapshotJson)) <> @FeatureSnapshotHash
        THROW 52318, 'I2026 feature snapshot hash does not match its JSON payload.', 1;

    DECLARE @SourceMarketType NVARCHAR(50) = CASE @MarketType
        WHEN N'TotalGoals' THEN N'GoalsTotal'
        WHEN N'TotalCorners' THEN N'CornersTotal'
    END;
    IF @SourceMarketType IS NULL
        THROW 52313, 'I2026 supports total goals and total corners only.', 1;

    DECLARE
        @StoredOpeningCapturedAtUtc DATETIME2(3),
        @StoredOpeningLine DECIMAL(10,2),
        @StoredOpeningOverOdds DECIMAL(18,6),
        @StoredOpeningUnderOdds DECIMAL(18,6),
        @StoredCurrentCapturedAtUtc DATETIME2(3),
        @StoredCurrentLine DECIMAL(10,2),
        @StoredCurrentOverOdds DECIMAL(18,6),
        @StoredCurrentUnderOdds DECIMAL(18,6);

    SELECT
        @StoredOpeningCapturedAtUtc = snapshot.CapturedAtUtc,
        @StoredOpeningLine = snapshot.LineValue,
        @StoredOpeningOverOdds = snapshot.OverOdds,
        @StoredOpeningUnderOdds = snapshot.UnderOdds
    FROM dbo.CornerOddsSnapshots AS snapshot
    WHERE snapshot.CornerOddsSnapshotId = @OpeningSnapshotId
      AND snapshot.Source = @Source
      AND snapshot.MarketType = @SourceMarketType;

    SELECT
        @StoredCurrentCapturedAtUtc = snapshot.CapturedAtUtc,
        @StoredCurrentLine = snapshot.LineValue,
        @StoredCurrentOverOdds = snapshot.OverOdds,
        @StoredCurrentUnderOdds = snapshot.UnderOdds
    FROM dbo.CornerOddsSnapshots AS snapshot
    WHERE snapshot.CornerOddsSnapshotId = @CurrentSnapshotId
      AND snapshot.Source = @Source
      AND snapshot.MarketType = @SourceMarketType;

    IF @StoredOpeningCapturedAtUtc IS NULL OR @StoredCurrentCapturedAtUtc IS NULL
       OR @StoredOpeningCapturedAtUtc <> @OpeningCapturedAtUtc
       OR @StoredCurrentCapturedAtUtc <> @CurrentCapturedAtUtc
       OR @StoredOpeningLine <> @OpeningLine
       OR @StoredCurrentLine <> @CurrentLine
       OR @StoredOpeningOverOdds <= 1 OR @StoredOpeningUnderOdds <= 1
       OR @StoredCurrentOverOdds <= 1 OR @StoredCurrentUnderOdds <= 1
        THROW 52314, 'I2026 immutable opening/current snapshots do not match.', 1;

    DECLARE @ComputedOpeningOverNoVig DECIMAL(12,8) = CONVERT(DECIMAL(12,8),
        (1.0 / @StoredOpeningOverOdds) /
        ((1.0 / @StoredOpeningOverOdds) + (1.0 / @StoredOpeningUnderOdds)));
    DECLARE @ComputedCurrentOverNoVig DECIMAL(12,8) = CONVERT(DECIMAL(12,8),
        (1.0 / @StoredCurrentOverOdds) /
        ((1.0 / @StoredCurrentOverOdds) + (1.0 / @StoredCurrentUnderOdds)));
    DECLARE @ComputedSelectedOdds DECIMAL(18,6) = CASE @Selection
        WHEN N'Over' THEN @StoredCurrentOverOdds
        WHEN N'Under' THEN @StoredCurrentUnderOdds
    END;

    IF ABS(@ComputedOpeningOverNoVig - @OpeningOverNoVigProbability) > 0.000020
       OR ABS(@ComputedCurrentOverNoVig - @CurrentOverNoVigProbability) > 0.000020
       OR @ComputedSelectedOdds IS NULL
       OR ABS(@ComputedSelectedOdds - @SelectedOdds) > 0.000020
        THROW 52315, 'I2026 no-vig or selected odds do not match immutable snapshots.', 1;

    IF @PeerSnapshotId IS NOT NULL
    BEGIN
        DECLARE
            @StoredPeerSource NVARCHAR(50),
            @StoredPeerCapturedAtUtc DATETIME2(3),
            @StoredPeerLine DECIMAL(10,2),
            @StoredPeerOverOdds DECIMAL(18,6),
            @StoredPeerUnderOdds DECIMAL(18,6);
        SELECT
            @StoredPeerSource = snapshot.Source,
            @StoredPeerCapturedAtUtc = snapshot.CapturedAtUtc,
            @StoredPeerLine = snapshot.LineValue,
            @StoredPeerOverOdds = snapshot.OverOdds,
            @StoredPeerUnderOdds = snapshot.UnderOdds
        FROM dbo.CornerOddsSnapshots AS snapshot
        WHERE snapshot.CornerOddsSnapshotId = @PeerSnapshotId
          AND snapshot.MarketType = @SourceMarketType;

        DECLARE @ComputedPeerOverNoVig DECIMAL(12,8) = CASE
            WHEN @StoredPeerOverOdds > 1 AND @StoredPeerUnderOdds > 1 THEN CONVERT(DECIMAL(12,8),
                (1.0 / @StoredPeerOverOdds) /
                ((1.0 / @StoredPeerOverOdds) + (1.0 / @StoredPeerUnderOdds)))
        END;
        IF @StoredPeerSource IS NULL OR @StoredPeerSource = @Source
           OR @StoredPeerSource <> @PeerSource
           OR @StoredPeerCapturedAtUtc <> @PeerCapturedAtUtc
           OR @StoredPeerLine <> @PeerLine
           OR @ComputedPeerOverNoVig IS NULL
           OR ABS(@ComputedPeerOverNoVig - @PeerOverNoVigProbability) > 0.000020
            THROW 52316, 'I2026 peer snapshot does not match immutable evidence.', 1;
    END;

    DECLARE @WasInserted BIT = 0;
    DECLARE @ShadowEvaluationId BIGINT;
    DECLARE @ExistingFeatureSnapshotHash BINARY(32);
    DECLARE @ExistingDecision NVARCHAR(20);
    BEGIN TRANSACTION;
    SELECT
        @ShadowEvaluationId = ShadowEvaluationId,
        @ExistingFeatureSnapshotHash = FeatureSnapshotHash,
        @ExistingDecision = Decision
    FROM dbo.BotI2026ShadowEvaluations WITH (UPDLOCK, HOLDLOCK)
    WHERE IdempotencyKey = @IdempotencyKey;

    IF @ShadowEvaluationId IS NOT NULL
       AND (@ExistingFeatureSnapshotHash <> @FeatureSnapshotHash OR @ExistingDecision <> @Decision)
        THROW 52319, 'I2026 idempotent replay changed decision evidence without a configuration-version bump.', 1;

    IF @ShadowEvaluationId IS NULL
    BEGIN
        INSERT dbo.BotI2026ShadowEvaluations
        (
            IdempotencyKey, BotKey, ConfigurationVersion, FeatureSchemaVersion,
            FixtureIdentity, ApiFootballFixtureId, FixtureDateUtc,
            PredictionTimestampUtc, League, HomeTeam, AwayTeam, Source,
            SourceMatchId, MarketType, Selection, Decision, SignalScore,
            SelectedOdds, OpeningSnapshotId, CurrentSnapshotId, PeerSnapshotId,
            OpeningCapturedAtUtc, CurrentCapturedAtUtc, PeerCapturedAtUtc,
            OpeningLine, CurrentLine, PeerLine, OpeningOverNoVigProbability,
            CurrentOverNoVigProbability, PeerOverNoVigProbability,
            SelectedProbabilityMovement, SelectedLineMovement,
            MovementVelocityPerHour, ObservationHours, OddsAgeMinutes,
            SnapshotCount, PeerSource, PinnacleOverNoVigProbability,
            BetanoOverNoVigProbability, CrossBookProbabilityDispersion,
            CrossBookLineDispersion, ReasonCodesJson, RiskFlagsJson,
            Explanation, FeatureSnapshotJson, FeatureSnapshotHash,
            ShadowOnly, PublicationBlocked
        )
        VALUES
        (
            @IdempotencyKey, N'I2026', @ConfigurationVersion, @FeatureSchemaVersion,
            @FixtureIdentity, @ApiFootballFixtureId, @FixtureDateUtc,
            @PredictionTimestampUtc, @League, @HomeTeam, @AwayTeam, @Source,
            @SourceMatchId, @MarketType, @Selection, @Decision, @SignalScore,
            @SelectedOdds, @OpeningSnapshotId, @CurrentSnapshotId, @PeerSnapshotId,
            @OpeningCapturedAtUtc, @CurrentCapturedAtUtc, @PeerCapturedAtUtc,
            @OpeningLine, @CurrentLine, @PeerLine, @OpeningOverNoVigProbability,
            @CurrentOverNoVigProbability, @PeerOverNoVigProbability,
            @SelectedProbabilityMovement, @SelectedLineMovement,
            @MovementVelocityPerHour, @ObservationHours, @OddsAgeMinutes,
            @SnapshotCount, @PeerSource, @PinnacleOverNoVigProbability,
            @BetanoOverNoVigProbability, @CrossBookProbabilityDispersion,
            @CrossBookLineDispersion, @ReasonCodesJson, @RiskFlagsJson,
            @Explanation, @FeatureSnapshotJson, @FeatureSnapshotHash, 1, 1
        );
        SET @ShadowEvaluationId = SCOPE_IDENTITY();
        SET @WasInserted = 1;
    END;
    COMMIT TRANSACTION;

    SELECT @ShadowEvaluationId AS ShadowEvaluationId, @WasInserted AS WasInserted;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_BotI2026ShadowEvaluations_Immutable
ON dbo.BotI2026ShadowEvaluations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52320, 'I2026 shadow evidence is append-only and cannot be updated or deleted.', 1;
END;

GO

CREATE OR ALTER TRIGGER dbo.trg_AutomatedCornerBetSelections_BlockI2026
ON dbo.AutomatedCornerBetSelections
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE BotKey = N'I2026')
        THROW 52321, 'I2026 is a permanent shadow experiment and cannot publish selections.', 1;
END;

GO

/*
    Outcome projection is read-only. It accepts only one exact official fixture,
    final FT/AET/PEN status, the market-specific availability flag and an API
    timestamp strictly later than prediction but no later than @AsOfUtc.
*/
CREATE OR ALTER FUNCTION dbo.fn_BotI2026ShadowLab
(
    @AsOfUtc DATETIME2(3)
)
RETURNS TABLE
AS
RETURN
(
    WITH IdentityCounts AS
    (
        SELECT
            shadow.ShadowEvaluationId,
            MatchCandidateCount = COUNT_BIG(history.Id),
            MatchHistoryId = MAX(CONVERT(BIGINT, history.Id))
        FROM dbo.BotI2026ShadowEvaluations AS shadow
        LEFT JOIN dbo.MatchHistory AS history
          ON shadow.ApiFootballFixtureId IS NOT NULL
         AND history.ApiFootballFixtureId = shadow.ApiFootballFixtureId
        WHERE shadow.PredictionTimestampUtc <= @AsOfUtc
        GROUP BY shadow.ShadowEvaluationId
    ),
    Outcome AS
    (
        SELECT
            shadow.*,
            identityCount.MatchCandidateCount,
            identityCount.MatchHistoryId,
            history.FixtureStatus,
            history.ApiFootballUpdatedAtUtc AS OutcomeAvailableUtc,
            history.ApiFootballGoalsAvailable,
            history.ApiFootballCornersAvailable,
            ActualValue = CONVERT(INT, CASE shadow.MarketType
                WHEN N'TotalGoals' THEN history.HomeGoals + history.AwayGoals
                WHEN N'TotalCorners' THEN history.HomeCorners + history.AwayCorners
            END),
            SettlementState = CASE
                WHEN shadow.Decision <> N'Approved' THEN N'NotSelected'
                WHEN shadow.ApiFootballFixtureId IS NULL THEN
                    CASE WHEN @AsOfUtc < shadow.FixtureDateUtc THEN N'Pending' ELSE N'OfficialFixtureMissing' END
                WHEN identityCount.MatchCandidateCount = 0 THEN
                    CASE WHEN @AsOfUtc < shadow.FixtureDateUtc THEN N'Pending' ELSE N'Unmatched' END
                WHEN identityCount.MatchCandidateCount > 1 THEN N'Ambiguous'
                WHEN UPPER(LTRIM(RTRIM(COALESCE(history.FixtureStatus, N'')))) NOT IN (N'FT', N'AET', N'PEN')
                    THEN N'Pending'
                WHEN shadow.MarketType = N'TotalGoals'
                     AND (ISNULL(history.ApiFootballGoalsAvailable, 0) <> 1
                          OR history.HomeGoals IS NULL OR history.AwayGoals IS NULL)
                    THEN N'Pending'
                WHEN shadow.MarketType = N'TotalCorners'
                     AND (ISNULL(history.ApiFootballCornersAvailable, 0) <> 1
                          OR history.HomeCorners IS NULL OR history.AwayCorners IS NULL)
                    THEN N'Pending'
                WHEN history.ApiFootballUpdatedAtUtc IS NULL THEN N'OutcomeTimestampMissing'
                WHEN history.ApiFootballUpdatedAtUtc <= shadow.PredictionTimestampUtc THEN N'TemporalRejected'
                WHEN history.ApiFootballUpdatedAtUtc > @AsOfUtc THEN N'Pending'
                ELSE N'Settled'
            END
        FROM dbo.BotI2026ShadowEvaluations AS shadow
        INNER JOIN IdentityCounts AS identityCount
          ON identityCount.ShadowEvaluationId = shadow.ShadowEvaluationId
        LEFT JOIN dbo.MatchHistory AS history
          ON history.Id = identityCount.MatchHistoryId
         AND identityCount.MatchCandidateCount = 1
        WHERE shadow.PredictionTimestampUtc <= @AsOfUtc
    ),
    Factorized AS
    (
        SELECT
            outcome.*,
            SettlementFactor = CASE WHEN outcome.SettlementState <> N'Settled' THEN NULL ELSE
                CONVERT(DECIMAL(9,4), CASE outcome.Selection
                    WHEN N'Over' THEN CASE WHEN outcome.ActualValue > outcome.CurrentLine THEN 1.0
                                           WHEN outcome.ActualValue = outcome.CurrentLine THEN 0.0 ELSE -1.0 END
                    WHEN N'Under' THEN CASE WHEN outcome.ActualValue < outcome.CurrentLine THEN 1.0
                                            WHEN outcome.ActualValue = outcome.CurrentLine THEN 0.0 ELSE -1.0 END
                END) END
        FROM Outcome AS outcome
    )
    SELECT
        factor.*,
        Result = CASE factor.SettlementFactor
            WHEN 1.0000 THEN N'Win'
            WHEN 0.5000 THEN N'HalfWin'
            WHEN 0.0000 THEN N'Push'
            WHEN -0.5000 THEN N'HalfLoss'
            WHEN -1.0000 THEN N'Loss'
        END,
        ProfitLoss = CONVERT(DECIMAL(12,4), CASE factor.SettlementFactor
            WHEN 1.0000 THEN factor.SelectedOdds - 1.0
            WHEN 0.5000 THEN (factor.SelectedOdds - 1.0) / 2.0
            WHEN 0.0000 THEN 0.0
            WHEN -0.5000 THEN -0.5
            WHEN -1.0000 THEN -1.0
        END)
    FROM Factorized AS factor
);

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotI2026ShadowStatus
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        BotKey = N'I2026',
        ConfigurationVersion = N'bot-i-market-movement-shadow-1.0.0',
        FeatureSchemaVersion = N'bot-i-market-movement-features-1.0.0',
        SchemaReady = CONVERT(BIT, 1),
        ShadowOnly = CONVERT(BIT, 1),
        PublicationBlocked = CONVERT(BIT, 1),
        Evaluations = COUNT_BIG(*),
        Approved = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Approved' THEN 1 ELSE 0 END)), 0),
        Rejected = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Rejected' THEN 1 ELSE 0 END)), 0),
        Abstained = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN Decision = N'Abstain' THEN 1 ELSE 0 END)), 0),
        UnsafeRows = COALESCE(SUM(CONVERT(BIGINT, CASE WHEN ShadowOnly <> 1 OR PublicationBlocked <> 1 THEN 1 ELSE 0 END)), 0),
        FirstPredictionTimestampUtc = MIN(PredictionTimestampUtc),
        LastPredictionTimestampUtc = MAX(PredictionTimestampUtc),
        State = N'SHADOW_ONLY'
    FROM dbo.BotI2026ShadowEvaluations;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotI2026ShadowEvaluations
    @PredictionFromUtc DATETIME2(3) = NULL,
    @PredictionToUtc DATETIME2(3) = NULL,
    @AsOfUtc DATETIME2(3),
    @Decision NVARCHAR(20) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @Selection NVARCHAR(10) = NULL,
    @Source NVARCHAR(50) = NULL,
    @ConfigurationVersion NVARCHAR(80) = NULL,
    @Page INT = 1,
    @PageSize INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH Filtered AS
    (
        SELECT lab.*, TotalRows = COUNT_BIG(*) OVER ()
        FROM dbo.fn_BotI2026ShadowLab(@AsOfUtc) AS lab
        WHERE (@PredictionFromUtc IS NULL OR lab.PredictionTimestampUtc >= @PredictionFromUtc)
          AND (@PredictionToUtc IS NULL OR lab.PredictionTimestampUtc < @PredictionToUtc)
          AND (@Decision IS NULL OR lab.Decision = @Decision)
          AND (@MarketType IS NULL OR lab.MarketType = @MarketType)
          AND (@Selection IS NULL OR lab.Selection = @Selection)
          AND (@Source IS NULL OR lab.Source = @Source)
          AND (@ConfigurationVersion IS NULL OR lab.ConfigurationVersion = @ConfigurationVersion)
    )
    SELECT *
    FROM Filtered
    ORDER BY PredictionTimestampUtc DESC, ShadowEvaluationId DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;

GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBotI2026ShadowScorecards
    @AsOfUtc DATETIME2(3),
    @ConfigurationVersion NVARCHAR(80) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Windows TABLE(WindowDays INT NOT NULL PRIMARY KEY);
    INSERT INTO @Windows(WindowDays) VALUES (7), (30), (90);

    ;WITH Lab AS
    (
        SELECT
            lab.*,
            ApprovedSequence = CASE WHEN lab.Decision = N'Approved' THEN ROW_NUMBER() OVER
            (
                PARTITION BY lab.FixtureIdentity, lab.ConfigurationVersion, lab.Decision
                ORDER BY lab.PredictionTimestampUtc, lab.ShadowEvaluationId
            ) END
        FROM dbo.fn_BotI2026ShadowLab(@AsOfUtc) AS lab
        WHERE lab.PredictionTimestampUtc > DATEADD(DAY, -90, @AsOfUtc)
          AND (@ConfigurationVersion IS NULL OR lab.ConfigurationVersion = @ConfigurationVersion)
    ),
    Segments AS
    (
        SELECT lab.*, segment.Dimension, segment.Segment
        FROM Lab AS lab
        CROSS APPLY (VALUES
            (N'All', N'All'),
            (N'Configuration', lab.ConfigurationVersion),
            (N'Market', lab.MarketType),
            (N'Source', lab.Source)
        ) AS segment(Dimension, Segment)
    ),
    Aggregated AS
    (
        SELECT
            window.WindowDays,
            DateFromUtc = DATEADD(DAY, -window.WindowDays, @AsOfUtc),
            DateToUtc = @AsOfUtc,
            segment.Dimension,
            segment.Segment,
            Evaluations = COUNT_BIG(*),
            FixturesEvaluated = COUNT_BIG(DISTINCT segment.FixtureIdentity),
            Approved = SUM(CONVERT(BIGINT, CASE WHEN segment.Decision = N'Approved' THEN 1 ELSE 0 END)),
            Rejected = SUM(CONVERT(BIGINT, CASE WHEN segment.Decision = N'Rejected' THEN 1 ELSE 0 END)),
            Abstained = SUM(CONVERT(BIGINT, CASE WHEN segment.Decision = N'Abstain' THEN 1 ELSE 0 END)),
            Settled = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.SettlementState = N'Settled' THEN 1 ELSE 0 END)),
            Won = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.Result = N'Win' THEN 1 ELSE 0 END)),
            HalfWon = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.Result = N'HalfWin' THEN 1 ELSE 0 END)),
            Pushes = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.Result = N'Push' THEN 1 ELSE 0 END)),
            HalfLost = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.Result = N'HalfLoss' THEN 1 ELSE 0 END)),
            Lost = SUM(CONVERT(BIGINT, CASE WHEN segment.ApprovedSequence = 1 AND segment.Result = N'Loss' THEN 1 ELSE 0 END)),
            ProfitLoss = SUM(CASE WHEN segment.ApprovedSequence = 1 THEN segment.ProfitLoss END),
            CrossBookRows = SUM(CONVERT(BIGINT, CASE WHEN segment.PeerSnapshotId IS NOT NULL THEN 1 ELSE 0 END)),
            AverageSignalScore = AVG(CONVERT(FLOAT, segment.SignalScore)),
            AverageAbsoluteProbabilityMovement = AVG(ABS(CONVERT(FLOAT, segment.SelectedProbabilityMovement))),
            AverageAbsoluteLineMovement = AVG(ABS(CONVERT(FLOAT, segment.SelectedLineMovement))),
            AverageOddsAgeMinutes = AVG(CONVERT(FLOAT, segment.OddsAgeMinutes)),
            AverageObservationHours = AVG(CONVERT(FLOAT, segment.ObservationHours))
        FROM @Windows AS window
        INNER JOIN Segments AS segment
          ON segment.PredictionTimestampUtc > DATEADD(DAY, -window.WindowDays, @AsOfUtc)
         AND segment.PredictionTimestampUtc <= @AsOfUtc
        GROUP BY window.WindowDays, segment.Dimension, segment.Segment
    )
    SELECT
        aggregated.WindowDays,
        aggregated.DateFromUtc,
        aggregated.DateToUtc,
        aggregated.Dimension,
        aggregated.Segment,
        aggregated.Evaluations,
        aggregated.FixturesEvaluated,
        aggregated.Approved,
        aggregated.Rejected,
        aggregated.Abstained,
        aggregated.Settled,
        aggregated.Won,
        aggregated.HalfWon,
        aggregated.Pushes,
        aggregated.HalfLost,
        aggregated.Lost,
        ApprovalRate = CONVERT(FLOAT, aggregated.Approved) / NULLIF(aggregated.Evaluations, 0),
        CrossBookCoverageRate = CONVERT(FLOAT, aggregated.CrossBookRows) / NULLIF(aggregated.Evaluations, 0),
        Stake = CONVERT(FLOAT, aggregated.Settled),
        ProfitLoss = CONVERT(FLOAT, aggregated.ProfitLoss),
        Yield = CONVERT(FLOAT, aggregated.ProfitLoss) / NULLIF(CONVERT(FLOAT, aggregated.Settled), 0),
        aggregated.AverageSignalScore,
        aggregated.AverageAbsoluteProbabilityMovement,
        aggregated.AverageAbsoluteLineMovement,
        aggregated.AverageOddsAgeMinutes,
        aggregated.AverageObservationHours,
        Deployable = CONVERT(BIT, 0),
        PromotionState = N'SHADOW_ONLY',
        ScorecardType = N'OUTCOME_AWARE_SHADOW_OFFICIAL_FIXTURE_ONLY'
    FROM Aggregated AS aggregated
    ORDER BY aggregated.WindowDays, aggregated.Dimension, aggregated.Segment;
END;

GO
