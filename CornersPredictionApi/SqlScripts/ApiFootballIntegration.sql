SET NOCOUNT ON;
SET XACT_ABORT ON;

-- A finished fixture may expose goals without advanced statistics. NULL means
-- "not supplied/verified" and must never be replaced with a synthetic zero.
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballGoalsAvailable') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballGoalsAvailable BIT NULL;
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballCornersAvailable') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballCornersAvailable BIT NULL;
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballShotsAvailable') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballShotsAvailable BIT NULL;
IF COL_LENGTH(N'dbo.MatchHistory', N'ApiFootballShotsOnGoalAvailable') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballShotsOnGoalAvailable BIT NULL;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'HomeCorners' AND is_nullable = 0)
BEGIN
    DROP INDEX IF EXISTS IX_MatchHistory_BotPickSettlement ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_PredictionContext_StdHome ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_PredictionContext_StdAway ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_PredictionContext_RawHome ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_PredictionContext_RawAway ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_AsOf_StdHome ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_AsOf_StdAway ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_AsOf_RawHome ON dbo.MatchHistory;
    DROP INDEX IF EXISTS IX_MatchHistory_AsOf_RawAway ON dbo.MatchHistory;
END;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'HomeCorners' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN HomeCorners INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'AwayCorners' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN AwayCorners INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'HomeShots' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN HomeShots INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'AwayShots' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN AwayShots INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'HomeShotsOnGoal' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN HomeShotsOnGoal INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'AwayShotsOnGoal' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN AwayShotsOnGoal INT NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'HomePossession' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN HomePossession DECIMAL(5,2) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MatchHistory') AND name = N'AwayPossession' AND is_nullable = 0)
    ALTER TABLE dbo.MatchHistory ALTER COLUMN AwayPossession DECIMAL(5,2) NULL;

IF COL_LENGTH('dbo.MatchHistory', 'DataSource') IS NULL
    ALTER TABLE dbo.MatchHistory ADD DataSource NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'ApiFootballFixtureId') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballFixtureId BIGINT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'ApiFootballLeagueId') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballLeagueId INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'ApiFootballHomeTeamId') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballHomeTeamId INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'ApiFootballAwayTeamId') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballAwayTeamId INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'FixtureRound') IS NULL
    ALTER TABLE dbo.MatchHistory ADD FixtureRound NVARCHAR(120) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'FixtureStatus') IS NULL
    ALTER TABLE dbo.MatchHistory ADD FixtureStatus NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'VenueName') IS NULL
    ALTER TABLE dbo.MatchHistory ADD VenueName NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'VenueCity') IS NULL
    ALTER TABLE dbo.MatchHistory ADD VenueCity NVARCHAR(120) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'Referee') IS NULL
    ALTER TABLE dbo.MatchHistory ADD Referee NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeHalfTimeGoals') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeHalfTimeGoals INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayHalfTimeGoals') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayHalfTimeGoals INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeFouls') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeFouls INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayFouls') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayFouls INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeOffsides') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeOffsides INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayOffsides') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayOffsides INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeYellowCards') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeYellowCards INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayYellowCards') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayYellowCards INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeRedCards') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeRedCards INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayRedCards') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayRedCards INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomeTotalPasses') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomeTotalPasses INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayTotalPasses') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayTotalPasses INT NULL;
IF COL_LENGTH('dbo.MatchHistory', 'HomePassAccuracy') IS NULL
    ALTER TABLE dbo.MatchHistory ADD HomePassAccuracy DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'AwayPassAccuracy') IS NULL
    ALTER TABLE dbo.MatchHistory ADD AwayPassAccuracy DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.MatchHistory', 'ApiFootballUpdatedAtUtc') IS NULL
    ALTER TABLE dbo.MatchHistory ADD ApiFootballUpdatedAtUtc DATETIME2(0) NULL;

-- API-Football uses names such as Damallsvenskan and Toppserien that do not
-- explicitly contain "Women". Keep those rows available but out of the men's model.
UPDATE dbo.MatchHistory
SET HomeTeamGender = 'F', AwayTeamGender = 'F'
WHERE ApiFootballLeagueId IN
(
    44, 64, 82, 254, 549, 640, 641, 649, 660, 666, 673, 725, 736,
    915, 918, 1013, 1103, 1117, 1136, 1182, 1189, 1229
)
AND (ISNULL(HomeTeamGender, '') <> 'F' OR ISNULL(AwayTeamGender, '') <> 'F');

CREATE TABLE #ApiFootballCanonicalCandidates
(
    Id BIGINT NOT NULL PRIMARY KEY,
    MatchDate DATE NOT NULL,
    Season NVARCHAR(50) NOT NULL,
    CanonicalLeague NVARCHAR(300) NULL,
    CanonicalHomeTeam NVARCHAR(300) NULL,
    CanonicalAwayTeam NVARCHAR(300) NULL,
    HomeTeamGender NVARCHAR(20) NOT NULL,
    AwayTeamGender NVARCHAR(20) NOT NULL,
    ApiFootballFixtureId BIGINT NULL
);

INSERT #ApiFootballCanonicalCandidates
    (Id, MatchDate, Season, CanonicalLeague, CanonicalHomeTeam, CanonicalAwayTeam,
     HomeTeamGender, AwayTeamGender, ApiFootballFixtureId)
SELECT
    Id,
    MatchDate,
    Season,
    dbo.fn_CanonicalLeagueName(COALESCE(NULLIF(StandardizedLeague, ''), League)),
    dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, ''), HomeTeam)),
    dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, ''), AwayTeam)),
    ISNULL(HomeTeamGender, 'M'),
    ISNULL(AwayTeamGender, 'M'),
    ApiFootballFixtureId
FROM dbo.MatchHistory;

CREATE INDEX IX_ApiFootballCanonicalCandidates_DateSeason
    ON #ApiFootballCanonicalCandidates(MatchDate, Season);

CREATE TABLE #ApiFootballCanonicalDuplicates
(
    DuplicateId BIGINT NOT NULL PRIMARY KEY,
    KeeperId BIGINT NOT NULL,
    ApiFootballFixtureId BIGINT NULL,
    SourceMatchId NVARCHAR(100) NULL
);

INSERT #ApiFootballCanonicalDuplicates
    (DuplicateId, KeeperId, ApiFootballFixtureId, SourceMatchId)
SELECT
    source.Id,
    keeper.Id,
    source.ApiFootballFixtureId,
    source.SourceMatchId
FROM dbo.MatchHistory source
CROSS APPLY
(
    SELECT TOP (1) candidate.Id
    FROM #ApiFootballCanonicalCandidates candidate
    WHERE candidate.Id <> source.Id
      AND candidate.ApiFootballFixtureId IS NULL
      AND candidate.MatchDate = source.MatchDate
      AND candidate.CanonicalHomeTeam COLLATE Latin1_General_100_CI_AI =
          dbo.fn_CanonicalTeamName(COALESCE(NULLIF(source.StandardizedHomeTeam, ''), source.HomeTeam)) COLLATE Latin1_General_100_CI_AI
      AND candidate.CanonicalAwayTeam COLLATE Latin1_General_100_CI_AI =
          dbo.fn_CanonicalTeamName(COALESCE(NULLIF(source.StandardizedAwayTeam, ''), source.AwayTeam)) COLLATE Latin1_General_100_CI_AI
      AND candidate.HomeTeamGender = ISNULL(source.HomeTeamGender, 'M')
      AND candidate.AwayTeamGender = ISNULL(source.AwayTeamGender, 'M')
    ORDER BY CASE WHEN candidate.ApiFootballFixtureId = source.ApiFootballFixtureId THEN 0 ELSE 1 END,
             candidate.Id
) keeper
WHERE source.ApiFootballFixtureId IS NOT NULL;

UPDATE duplicate
SET ApiFootballFixtureId = NULL,
    SourceMatchId = NULL
FROM dbo.MatchHistory duplicate
INNER JOIN #ApiFootballCanonicalDuplicates mapping ON mapping.DuplicateId = duplicate.Id;

UPDATE keeper
SET HomeFormation = COALESCE(duplicate.HomeFormation, keeper.HomeFormation),
    AwayFormation = COALESCE(duplicate.AwayFormation, keeper.AwayFormation),
    HomeGoals = COALESCE(duplicate.HomeGoals, keeper.HomeGoals),
    AwayGoals = COALESCE(duplicate.AwayGoals, keeper.AwayGoals),
    HomeCorners = COALESCE(duplicate.HomeCorners, keeper.HomeCorners),
    AwayCorners = COALESCE(duplicate.AwayCorners, keeper.AwayCorners),
    HomeShots = COALESCE(duplicate.HomeShots, keeper.HomeShots),
    AwayShots = COALESCE(duplicate.AwayShots, keeper.AwayShots),
    HomeShotsOnGoal = COALESCE(duplicate.HomeShotsOnGoal, keeper.HomeShotsOnGoal),
    AwayShotsOnGoal = COALESCE(duplicate.AwayShotsOnGoal, keeper.AwayShotsOnGoal),
    HomePossession = COALESCE(duplicate.HomePossession, keeper.HomePossession),
    AwayPossession = COALESCE(duplicate.AwayPossession, keeper.AwayPossession),
    SourceMatchId = COALESCE(keeper.SourceMatchId, mapping.SourceMatchId),
    DataSource = COALESCE(duplicate.DataSource, keeper.DataSource, 'API-Football'),
    ApiFootballFixtureId = mapping.ApiFootballFixtureId,
    ApiFootballLeagueId = duplicate.ApiFootballLeagueId,
    ApiFootballHomeTeamId = duplicate.ApiFootballHomeTeamId,
    ApiFootballAwayTeamId = duplicate.ApiFootballAwayTeamId,
    FixtureRound = COALESCE(duplicate.FixtureRound, keeper.FixtureRound),
    FixtureStatus = COALESCE(duplicate.FixtureStatus, keeper.FixtureStatus),
    VenueName = COALESCE(duplicate.VenueName, keeper.VenueName),
    VenueCity = COALESCE(duplicate.VenueCity, keeper.VenueCity),
    Referee = COALESCE(duplicate.Referee, keeper.Referee),
    HomeHalfTimeGoals = COALESCE(duplicate.HomeHalfTimeGoals, keeper.HomeHalfTimeGoals),
    AwayHalfTimeGoals = COALESCE(duplicate.AwayHalfTimeGoals, keeper.AwayHalfTimeGoals),
    HomeFouls = COALESCE(duplicate.HomeFouls, keeper.HomeFouls),
    AwayFouls = COALESCE(duplicate.AwayFouls, keeper.AwayFouls),
    HomeOffsides = COALESCE(duplicate.HomeOffsides, keeper.HomeOffsides),
    AwayOffsides = COALESCE(duplicate.AwayOffsides, keeper.AwayOffsides),
    HomeYellowCards = COALESCE(duplicate.HomeYellowCards, keeper.HomeYellowCards),
    AwayYellowCards = COALESCE(duplicate.AwayYellowCards, keeper.AwayYellowCards),
    HomeRedCards = COALESCE(duplicate.HomeRedCards, keeper.HomeRedCards),
    AwayRedCards = COALESCE(duplicate.AwayRedCards, keeper.AwayRedCards),
    HomeTotalPasses = COALESCE(duplicate.HomeTotalPasses, keeper.HomeTotalPasses),
    AwayTotalPasses = COALESCE(duplicate.AwayTotalPasses, keeper.AwayTotalPasses),
    HomePassAccuracy = COALESCE(duplicate.HomePassAccuracy, keeper.HomePassAccuracy),
    AwayPassAccuracy = COALESCE(duplicate.AwayPassAccuracy, keeper.AwayPassAccuracy),
    ApiFootballUpdatedAtUtc = COALESCE(duplicate.ApiFootballUpdatedAtUtc, keeper.ApiFootballUpdatedAtUtc)
FROM dbo.MatchHistory keeper
INNER JOIN #ApiFootballCanonicalDuplicates mapping ON mapping.KeeperId = keeper.Id
INNER JOIN dbo.MatchHistory duplicate ON duplicate.Id = mapping.DuplicateId;

DELETE duplicate
FROM dbo.MatchHistory duplicate
INNER JOIN #ApiFootballCanonicalDuplicates mapping ON mapping.DuplicateId = duplicate.Id;

UPDATE history
SET League = canonical.DbLeagueName,
    StandardizedLeague = canonical.DbLeagueName
FROM dbo.MatchHistory history
INNER JOIN
(
    VALUES
        (11, N'Copa Sudamericana'),
        (13, N'Copa Libertadores'),
        (39, N'Premier League'),
        (40, N'English League Championship'),
        (41, N'English League One'),
        (42, N'English League Two'),
        (45, N'FA Cup'),
        (61, N'Ligue 1'),
        (62, N'Ligue 2'),
        (66, N'Coupe de France'),
        (73, N'Copa de Brasil'),
        (78, N'Bundesliga'),
        (81, N'DFB Pokal'),
        (88, N'Eredivisie'),
        (89, N'Eerste Divisie'),
        (94, N'Primeira Liga'),
        (98, N'J1 League'),
        (103, N'Eliteserien'),
        (113, N'Allsvenskan'),
        (119, N'Danish Superliga'),
        (128, N'Liga Profesional Argentina'),
        (135, N'Serie A'),
        (136, N'Italian Serie B'),
        (140, N'La Liga'),
        (141, N'Spanish LALIGA 2'),
        (144, N'Belgian Pro League'),
        (179, N'Scottish Premiership'),
        (203, N'Turkish Super Lig'),
        (207, N'Super League (Switzerland)'),
        (218, N'Austrian Bundesliga'),
        (250, N'Paraguayan Primera División'),
        (262, N'Liga MX'),
        (281, N'Liga 1 Peru'),
        (344, N'Bolivian Liga Profesional')
) canonical(ApiFootballLeagueId, DbLeagueName)
    ON canonical.ApiFootballLeagueId = history.ApiFootballLeagueId
WHERE ISNULL(history.League, '') <> canonical.DbLeagueName
   OR ISNULL(history.StandardizedLeague, '') <> canonical.DbLeagueName;

GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.MatchHistory')
      AND name = 'UX_MatchHistory_ApiFootballFixtureId'
)
BEGIN
    CREATE UNIQUE INDEX UX_MatchHistory_ApiFootballFixtureId
        ON dbo.MatchHistory(ApiFootballFixtureId)
        WHERE ApiFootballFixtureId IS NOT NULL;
END;

GO

IF OBJECT_ID('dbo.ApiFootballLeagueSeasons', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiFootballLeagueSeasons
    (
        ApiFootballLeagueId INT NOT NULL,
        Season INT NOT NULL,
        LeagueName NVARCHAR(200) NOT NULL,
        DbLeagueName NVARCHAR(200) NULL,
        Country NVARCHAR(120) NULL,
        CompetitionType NVARCHAR(30) NULL,
        IsCurrent BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_IsCurrent DEFAULT (0),
        CoverageEvents BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_Events DEFAULT (0),
        CoverageLineups BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_Lineups DEFAULT (0),
        CoverageFixtureStatistics BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_FixtureStats DEFAULT (0),
        CoveragePlayerStatistics BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_PlayerStats DEFAULT (0),
        CoverageStandings BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_Standings DEFAULT (0),
        CoveragePredictions BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_Predictions DEFAULT (0),
        CoverageOdds BIT NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_Odds DEFAULT (0),
        LastSyncedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_ApiFootballLeagueSeasons_LastSync DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ApiFootballLeagueSeasons PRIMARY KEY (ApiFootballLeagueId, Season)
    );
END;

IF OBJECT_ID('dbo.ApiFootballTeams', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiFootballTeams
    (
        ApiFootballTeamId INT NOT NULL CONSTRAINT PK_ApiFootballTeams PRIMARY KEY,
        SourceTeamName NVARCHAR(200) NOT NULL,
        StandardizedTeamName NVARCHAR(200) NULL,
        Country NVARCHAR(120) NULL,
        LogoUrl NVARCHAR(500) NULL,
        LastSyncedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_ApiFootballTeams_LastSync DEFAULT (SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('dbo.LeagueStandingSnapshots', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeagueStandingSnapshots
    (
        ApiFootballLeagueId INT NOT NULL,
        Season INT NOT NULL,
        SnapshotDate DATE NOT NULL,
        GroupName NVARCHAR(150) NOT NULL CONSTRAINT DF_LeagueStandingSnapshots_Group DEFAULT (''),
        ApiFootballTeamId INT NOT NULL,
        TeamName NVARCHAR(200) NOT NULL,
        RankPosition INT NOT NULL,
        Points INT NULL,
        GoalsDifference INT NULL,
        Played INT NULL,
        Won INT NULL,
        Drawn INT NULL,
        Lost INT NULL,
        GoalsFor INT NULL,
        GoalsAgainst INT NULL,
        RecentForm NVARCHAR(30) NULL,
        Description NVARCHAR(250) NULL,
        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_LeagueStandingSnapshots_Updated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_LeagueStandingSnapshots PRIMARY KEY
            (ApiFootballLeagueId, Season, SnapshotDate, GroupName, ApiFootballTeamId)
    );
END;

IF OBJECT_ID('dbo.ApiFootballSyncRuns', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiFootballSyncRuns
    (
        SyncRunId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ApiFootballSyncRuns PRIMARY KEY,
        ApiFootballLeagueId INT NOT NULL,
        Season INT NOT NULL,
        DateFrom DATE NULL,
        DateTo DATE NULL,
        DryRun BIT NOT NULL,
        Discovered INT NOT NULL,
        Processed INT NOT NULL,
        Inserted INT NOT NULL,
        Updated INT NOT NULL,
        Skipped INT NOT NULL,
        Errors INT NOT NULL,
        StartedAtUtc DATETIME2(0) NOT NULL,
        CompletedAtUtc DATETIME2(0) NOT NULL
    );
END;

IF OBJECT_ID('dbo.ApiFootballHistoricalCheckpoint', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiFootballHistoricalCheckpoint
    (
        CheckpointId TINYINT NOT NULL
            CONSTRAINT PK_ApiFootballHistoricalCheckpoint PRIMARY KEY
            CONSTRAINT CK_ApiFootballHistoricalCheckpoint_Singleton CHECK (CheckpointId = 1),
        MonthStart DATE NOT NULL,
        CompetitionOffset INT NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        StartedAtUtc DATETIME2(3) NULL,
        CompletedAtUtc DATETIME2(3) NULL,
        DiscoveredFixtures INT NULL,
        EligibleCompetitions INT NULL,
        ProcessedCompetitions INT NULL,
        ProcessedFixtures INT NULL,
        Inserted INT NULL,
        Updated INT NULL,
        Skipped INT NULL,
        Errors INT NULL,
        StoppedByQuota BIT NULL,
        DailyRemaining NVARCHAR(30) NULL,
        MinuteRemaining NVARCHAR(30) NULL,
        Message NVARCHAR(1000) NULL,
        UpdatedAtUtc DATETIME2(3) NOT NULL
            CONSTRAINT DF_ApiFootballHistoricalCheckpoint_Updated DEFAULT (SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.ApiFootballHistoricalCheckpoint WHERE CheckpointId = 1)
BEGIN
    INSERT dbo.ApiFootballHistoricalCheckpoint
        (CheckpointId, MonthStart, CompetitionOffset, Status, Message)
    VALUES
        (1, '2024-05-01', 542, N'Ready', N'Checkpoint recuperado de la sincronizacion historica anterior.');
END;

UPDATE leagueSeason
SET DbLeagueName = canonical.DbLeagueName
FROM dbo.ApiFootballLeagueSeasons leagueSeason
INNER JOIN
(
    VALUES
        (11, N'Copa Sudamericana'),
        (13, N'Copa Libertadores'),
        (39, N'Premier League'),
        (40, N'English League Championship'),
        (41, N'English League One'),
        (42, N'English League Two'),
        (45, N'FA Cup'),
        (61, N'Ligue 1'),
        (62, N'Ligue 2'),
        (66, N'Coupe de France'),
        (73, N'Copa de Brasil'),
        (78, N'Bundesliga'),
        (81, N'DFB Pokal'),
        (88, N'Eredivisie'),
        (89, N'Eerste Divisie'),
        (94, N'Primeira Liga'),
        (98, N'J1 League'),
        (103, N'Eliteserien'),
        (113, N'Allsvenskan'),
        (119, N'Danish Superliga'),
        (128, N'Liga Profesional Argentina'),
        (135, N'Serie A'),
        (136, N'Italian Serie B'),
        (140, N'La Liga'),
        (141, N'Spanish LALIGA 2'),
        (144, N'Belgian Pro League'),
        (179, N'Scottish Premiership'),
        (203, N'Turkish Super Lig'),
        (207, N'Super League (Switzerland)'),
        (218, N'Austrian Bundesliga'),
        (250, N'Paraguayan Primera División'),
        (262, N'Liga MX'),
        (281, N'Liga 1 Peru'),
        (344, N'Bolivian Liga Profesional')
) canonical(ApiFootballLeagueId, DbLeagueName)
    ON canonical.ApiFootballLeagueId = leagueSeason.ApiFootballLeagueId
WHERE ISNULL(leagueSeason.DbLeagueName, '') <> canonical.DbLeagueName;
