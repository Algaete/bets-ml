IF OBJECT_ID(N'dbo.BetanoUpcomingFootballOdds', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BetanoUpcomingFootballOdds
    (
        OddsId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BetanoUpcomingFootballOdds PRIMARY KEY,
        Bookmaker NVARCHAR(50) NOT NULL,
        SourceMatchId NVARCHAR(50) NULL,
        SourceUrl NVARCHAR(500) NOT NULL,
        MatchDateLocal DATETIME2(0) NULL,
        League NVARCHAR(150) NOT NULL,
        StandardizedLeague NVARCHAR(150) NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        StandardizedHomeTeam NVARCHAR(150) NULL,
        StandardizedAwayTeam NVARCHAR(150) NULL,
        HomeTeamGender VARCHAR(10) NOT NULL,
        AwayTeamGender VARCHAR(10) NOT NULL,
        CornersOver7_5 DECIMAL(10,2) NULL,
        CornersUnder7_5 DECIMAL(10,2) NULL,
        CornersOver8_5 DECIMAL(10,2) NULL,
        CornersUnder8_5 DECIMAL(10,2) NULL,
        CornersOver9_5 DECIMAL(10,2) NULL,
        CornersUnder9_5 DECIMAL(10,2) NULL,
        CornersOver10_5 DECIMAL(10,2) NULL,
        CornersUnder10_5 DECIMAL(10,2) NULL,
        ShotsOnTargetOver7_5 DECIMAL(10,2) NULL,
        ShotsOnTargetUnder7_5 DECIMAL(10,2) NULL,
        ShotsOnTargetOver8_5 DECIMAL(10,2) NULL,
        ShotsOnTargetUnder8_5 DECIMAL(10,2) NULL,
        ShotsOnTargetOver9_5 DECIMAL(10,2) NULL,
        ShotsOnTargetUnder9_5 DECIMAL(10,2) NULL,
        ShotsOnTargetOver10_5 DECIMAL(10,2) NULL,
        ShotsOnTargetUnder10_5 DECIMAL(10,2) NULL,
        Notes NVARCHAR(MAX) NULL,
        ScrapedAtUtc DATETIME2(3) NOT NULL,
        CreatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_BetanoUpcomingFootballOdds_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_BetanoUpcomingFootballOdds_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_BetanoUpcomingFootballOdds_Bookmaker_SourceUrl'
      AND object_id = OBJECT_ID(N'dbo.BetanoUpcomingFootballOdds')
)
BEGIN
    CREATE UNIQUE INDEX UX_BetanoUpcomingFootballOdds_Bookmaker_SourceUrl
        ON dbo.BetanoUpcomingFootballOdds (Bookmaker, SourceUrl);
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertBetanoUpcomingFootballOdds
    @Bookmaker NVARCHAR(50),
    @SourceMatchId NVARCHAR(50) = NULL,
    @SourceUrl NVARCHAR(500),
    @MatchDateLocal DATETIME2(0) = NULL,
    @League NVARCHAR(150),
    @StandardizedLeague NVARCHAR(150) = NULL,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @StandardizedHomeTeam NVARCHAR(150) = NULL,
    @StandardizedAwayTeam NVARCHAR(150) = NULL,
    @HomeTeamGender VARCHAR(10),
    @AwayTeamGender VARCHAR(10),
    @CornersOver7_5 DECIMAL(10,2) = NULL,
    @CornersUnder7_5 DECIMAL(10,2) = NULL,
    @CornersOver8_5 DECIMAL(10,2) = NULL,
    @CornersUnder8_5 DECIMAL(10,2) = NULL,
    @CornersOver9_5 DECIMAL(10,2) = NULL,
    @CornersUnder9_5 DECIMAL(10,2) = NULL,
    @CornersOver10_5 DECIMAL(10,2) = NULL,
    @CornersUnder10_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetOver7_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetUnder7_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetOver8_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetUnder8_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetOver9_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetUnder9_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetOver10_5 DECIMAL(10,2) = NULL,
    @ShotsOnTargetUnder10_5 DECIMAL(10,2) = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @ScrapedAtUtc DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@Bookmaker)), '') IS NULL
        THROW 50031, 'Bookmaker es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@SourceUrl)), '') IS NULL
        THROW 50032, 'SourceUrl es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@League)), '') IS NULL
        THROW 50033, 'League es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@HomeTeam)), '') IS NULL
        THROW 50034, 'HomeTeam es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@AwayTeam)), '') IS NULL
        THROW 50035, 'AwayTeam es obligatorio.', 1;

    MERGE dbo.BetanoUpcomingFootballOdds WITH (HOLDLOCK) AS Target
    USING
    (
        SELECT
            LTRIM(RTRIM(@Bookmaker)) AS Bookmaker,
            NULLIF(LTRIM(RTRIM(@SourceMatchId)), '') AS SourceMatchId,
            LTRIM(RTRIM(@SourceUrl)) AS SourceUrl,
            @MatchDateLocal AS MatchDateLocal,
            LTRIM(RTRIM(@League)) AS League,
            NULLIF(LTRIM(RTRIM(@StandardizedLeague)), '') AS StandardizedLeague,
            LTRIM(RTRIM(@HomeTeam)) AS HomeTeam,
            LTRIM(RTRIM(@AwayTeam)) AS AwayTeam,
            NULLIF(LTRIM(RTRIM(@StandardizedHomeTeam)), '') AS StandardizedHomeTeam,
            NULLIF(LTRIM(RTRIM(@StandardizedAwayTeam)), '') AS StandardizedAwayTeam,
            LTRIM(RTRIM(@HomeTeamGender)) AS HomeTeamGender,
            LTRIM(RTRIM(@AwayTeamGender)) AS AwayTeamGender,
            @CornersOver7_5 AS CornersOver7_5,
            @CornersUnder7_5 AS CornersUnder7_5,
            @CornersOver8_5 AS CornersOver8_5,
            @CornersUnder8_5 AS CornersUnder8_5,
            @CornersOver9_5 AS CornersOver9_5,
            @CornersUnder9_5 AS CornersUnder9_5,
            @CornersOver10_5 AS CornersOver10_5,
            @CornersUnder10_5 AS CornersUnder10_5,
            @ShotsOnTargetOver7_5 AS ShotsOnTargetOver7_5,
            @ShotsOnTargetUnder7_5 AS ShotsOnTargetUnder7_5,
            @ShotsOnTargetOver8_5 AS ShotsOnTargetOver8_5,
            @ShotsOnTargetUnder8_5 AS ShotsOnTargetUnder8_5,
            @ShotsOnTargetOver9_5 AS ShotsOnTargetOver9_5,
            @ShotsOnTargetUnder9_5 AS ShotsOnTargetUnder9_5,
            @ShotsOnTargetOver10_5 AS ShotsOnTargetOver10_5,
            @ShotsOnTargetUnder10_5 AS ShotsOnTargetUnder10_5,
            @Notes AS Notes,
            @ScrapedAtUtc AS ScrapedAtUtc
    ) AS Source
    ON Target.Bookmaker = Source.Bookmaker
   AND Target.SourceUrl = Source.SourceUrl
    WHEN MATCHED THEN
        UPDATE SET
            Target.SourceMatchId = Source.SourceMatchId,
            Target.MatchDateLocal = Source.MatchDateLocal,
            Target.League = Source.League,
            Target.StandardizedLeague = Source.StandardizedLeague,
            Target.HomeTeam = Source.HomeTeam,
            Target.AwayTeam = Source.AwayTeam,
            Target.StandardizedHomeTeam = Source.StandardizedHomeTeam,
            Target.StandardizedAwayTeam = Source.StandardizedAwayTeam,
            Target.HomeTeamGender = Source.HomeTeamGender,
            Target.AwayTeamGender = Source.AwayTeamGender,
            Target.CornersOver7_5 = Source.CornersOver7_5,
            Target.CornersUnder7_5 = Source.CornersUnder7_5,
            Target.CornersOver8_5 = Source.CornersOver8_5,
            Target.CornersUnder8_5 = Source.CornersUnder8_5,
            Target.CornersOver9_5 = Source.CornersOver9_5,
            Target.CornersUnder9_5 = Source.CornersUnder9_5,
            Target.CornersOver10_5 = Source.CornersOver10_5,
            Target.CornersUnder10_5 = Source.CornersUnder10_5,
            Target.ShotsOnTargetOver7_5 = Source.ShotsOnTargetOver7_5,
            Target.ShotsOnTargetUnder7_5 = Source.ShotsOnTargetUnder7_5,
            Target.ShotsOnTargetOver8_5 = Source.ShotsOnTargetOver8_5,
            Target.ShotsOnTargetUnder8_5 = Source.ShotsOnTargetUnder8_5,
            Target.ShotsOnTargetOver9_5 = Source.ShotsOnTargetOver9_5,
            Target.ShotsOnTargetUnder9_5 = Source.ShotsOnTargetUnder9_5,
            Target.ShotsOnTargetOver10_5 = Source.ShotsOnTargetOver10_5,
            Target.ShotsOnTargetUnder10_5 = Source.ShotsOnTargetUnder10_5,
            Target.Notes = Source.Notes,
            Target.ScrapedAtUtc = Source.ScrapedAtUtc,
            Target.UpdatedAtUtc = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            Bookmaker,
            SourceMatchId,
            SourceUrl,
            MatchDateLocal,
            League,
            StandardizedLeague,
            HomeTeam,
            AwayTeam,
            StandardizedHomeTeam,
            StandardizedAwayTeam,
            HomeTeamGender,
            AwayTeamGender,
            CornersOver7_5,
            CornersUnder7_5,
            CornersOver8_5,
            CornersUnder8_5,
            CornersOver9_5,
            CornersUnder9_5,
            CornersOver10_5,
            CornersUnder10_5,
            ShotsOnTargetOver7_5,
            ShotsOnTargetUnder7_5,
            ShotsOnTargetOver8_5,
            ShotsOnTargetUnder8_5,
            ShotsOnTargetOver9_5,
            ShotsOnTargetUnder9_5,
            ShotsOnTargetOver10_5,
            ShotsOnTargetUnder10_5,
            Notes,
            ScrapedAtUtc,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            Source.Bookmaker,
            Source.SourceMatchId,
            Source.SourceUrl,
            Source.MatchDateLocal,
            Source.League,
            Source.StandardizedLeague,
            Source.HomeTeam,
            Source.AwayTeam,
            Source.StandardizedHomeTeam,
            Source.StandardizedAwayTeam,
            Source.HomeTeamGender,
            Source.AwayTeamGender,
            Source.CornersOver7_5,
            Source.CornersUnder7_5,
            Source.CornersOver8_5,
            Source.CornersUnder8_5,
            Source.CornersOver9_5,
            Source.CornersUnder9_5,
            Source.CornersOver10_5,
            Source.CornersUnder10_5,
            Source.ShotsOnTargetOver7_5,
            Source.ShotsOnTargetUnder7_5,
            Source.ShotsOnTargetOver8_5,
            Source.ShotsOnTargetUnder8_5,
            Source.ShotsOnTargetOver9_5,
            Source.ShotsOnTargetUnder9_5,
            Source.ShotsOnTargetOver10_5,
            Source.ShotsOnTargetUnder10_5,
            Source.Notes,
            Source.ScrapedAtUtc,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
END
GO
